using System.Security.Claims;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Events;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>Appointment use cases: book, change, move through the statuses, cancel — all audited.</summary>
    public class Reservations
    {
        private readonly IAppDbContext _db;
        private readonly SlotService _slots;
        private readonly QueueService _queue;
        private readonly Outbox _outbox;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public Reservations(IAppDbContext db, SlotService slots, QueueService queue, Outbox outbox, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _slots = slots;
            _queue = queue;
            _outbox = outbox;
            _audit = audit;
            _user = user;
        }

        /// <summary>
        /// Books an appointment; creates the patient card when the FİN is new (an existing card keeps its
        /// name and birthday, only the phone may be refreshed). <paramref name="formErrors"/> are the form's
        /// own field errors, reported together with these checks.
        /// </summary>
        public async Task<Result> CreateAsync(ReserveFormViewModel form, Patient? patient, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var (f, m) in formErrors) r.Fail(m, f);
            var (examTypes, start, slot) = await ValidateAsync(form, null, validateBirthday: patient == null, r, ct);
            if (!r.Ok) return r;

            var createdPatient = false;
            if (patient == null)
            {
                patient = new Patient
                {
                    PinCode = Patient.NormalizePin(form.PinCode),
                    Name = form.Name!.Trim(),
                    Surname = form.Surname!.Trim(),
                    Phone = form.Phone!.Trim(),
                    Birthday = form.Birthday!.Value.Date,
                    CreatedAt = ClinicTime.Now
                };
                _db.Patients.Add(patient);
                createdPatient = true;
            }
            else if (!string.IsNullOrWhiteSpace(form.Phone) && form.Phone.Trim() != patient.Phone)
            {
                patient.Phone = form.Phone.Trim();
            }

            var reserve = new Reserve
            {
                Patient = patient,
                DoctorId = form.DoctorId,
                RoomId = slot!.RoomId,
                AppointmentAt = start,
                DurationMinutes = slot.SlotMinutes,
                EquipmentId = SlotService.Shape(examTypes).EquipmentId,
                Source = Enum.IsDefined(form.Source) ? form.Source : ReserveSource.Reception,
                AddedDate = ClinicTime.Now,
                Status = await _db.ReserveStatuses.FirstAsync(rs => rs.Name == StatusNames.Waiting, ct),
                ReserveExaminationTypes = examTypes.Select(e => new ReserveExaminationType { ExaminationTypeId = e.Id, Price = e.Price }).ToList()
            };
            _db.Reserves.Add(reserve);
            await _db.SaveChangesAsync(ct);

            if (createdPatient)
                _audit.Log("Xəstə kartı yaradıldı", AuditEntities.Patient, patient.Id, $"{patient.FullName} ({patient.PinCode})");
            var doctorName = await _db.Doctors.Where(d => d.Id == reserve.DoctorId).Select(d => d.FullName).FirstOrDefaultAsync(ct);
            _audit.Log("Rezervasiya yaradıldı", AuditEntities.Reservation, reserve.Id,
                $"{patient.FullName} — {Fmt.DateTime(reserve.AppointmentAt)}, {doctorName}");
            return Result.Success($"{patient.FullName} — {Fmt.DateTime(reserve.AppointmentAt)} üçün rezervasiya yaradıldı", reserve.Id);
        }

        /// <summary>Changes time/doctor (before check-in) and the examinations; locked prices of kept examinations stay.</summary>
        public async Task<Result> UpdateAsync(Reserve reserve, ReserveFormViewModel form, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            if (!ReserveFlow.CanEdit(reserve))
                return r.Fail($"«{StatusNames.Label(reserve.Status?.Name)}» statusunda rezervasiyaya düzəliş etmək olmaz");
            foreach (var (f, m) in formErrors) r.Fail(m, f);
            var (examTypes, start, slot) = await ValidateAsync(form, reserve, validateBirthday: false, r, ct);
            if (!r.Ok) return r;

            var changes = new List<string>();
            if (reserve.AppointmentAt != start || reserve.DoctorId != form.DoctorId)
            {
                changes.Add($"vaxt {Fmt.DateTime(reserve.AppointmentAt)} → {Fmt.DateTime(start)}");
                reserve.AppointmentAt = start;
                if (reserve.DoctorId != form.DoctorId) changes.Add("həkim dəyişdi");
                reserve.DoctorId = form.DoctorId;
            }
            if (slot != null)
            {
                // New time, or the services' length/device changed: the visit takes what was checked.
                reserve.RoomId = slot.RoomId;
                reserve.DurationMinutes = slot.SlotMinutes;
                reserve.EquipmentId = SlotService.Shape(examTypes).EquipmentId;
            }

            var selectedIds = examTypes.Select(e => e.Id).ToHashSet();
            var items = reserve.ReserveExaminationTypes!;
            var removed = items.Where(i => !selectedIds.Contains(i.ExaminationTypeId)).ToList();
            _db.ReserveExaminationTypes.RemoveRange(removed);
            var existingIds = items.Select(i => i.ExaminationTypeId).ToHashSet();
            var added = examTypes.Where(e => !existingIds.Contains(e.Id)).ToList();
            foreach (var e in added)
                items.Add(new ReserveExaminationType { ExaminationTypeId = e.Id, Price = e.Price });
            if (removed.Count > 0) changes.Add("çıxarıldı: " + string.Join(", ", removed.Select(x => x.ExaminationType.Name)));
            if (added.Count > 0) changes.Add("əlavə edildi: " + string.Join(", ", added.Select(a => a.Name)));

            await _db.SaveChangesAsync(ct);
            _audit.Log("Rezervasiya dəyişdirildi", AuditEntities.Reservation, reserve.Id,
                $"{reserve.Patient.FullName}: " + (changes.Count > 0 ? string.Join("; ", changes) : "dəyişiklik yoxdur"));
            return Result.Success("Rezervasiya yeniləndi", reserve.Id);
        }

        /// <summary>Moves the visit to the next status (check-in assigns a queue ticket; completion takes the materials).</summary>
        public async Task<(Result Result, int? DoctorId)?> TransitionAsync(int id, string to, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var res = await _db.Reserves.Include(r => r.Status).Include(r => r.Patient).FirstOrDefaultAsync(rs => rs.Id == id, ct);
            if (res == null) return null;

            var now = ClinicTime.Now;
            if (!ReserveFlow.IsAllowed(res, to, user, now))
                return (Result.Failure($"«{StatusNames.Label(res.Status?.Name)}» statusundan «{StatusNames.Label(to)}» statusuna keçmək olmaz"), res.DoctorId);

            var from = res.Status?.Name;
            res.Status = await _db.ReserveStatuses.FirstAsync(rs => rs.Name == to, ct);
            switch (to)
            {
                case StatusNames.Confirmed: res.ConfirmDate = now; break;
                case StatusNames.Arrived:
                    res.ArrivedAt = now;
                    _queue.AssignTicket(res, now);
                    break;
                case StatusNames.InProgress: res.StartedAt = now; res.ArrivedAt ??= now; break;
                case StatusNames.Completed:
                    res.CompletedAt = now; res.StartedAt ??= now; res.ArrivedAt ??= now;
                    _outbox.Add(new VisitCompleted(res.Id, _user.UserName));
                    break;
            }
            await _db.SaveChangesAsync(ct);
            _audit.Log("Status dəyişdi", AuditEntities.Reservation, res.Id,
                $"{res.Patient.FullName}: {StatusNames.Label(from)} → {StatusNames.Label(to)}"
                + (to == StatusNames.Arrived ? $", talon {res.QueueTicket}" : ""));
            var result = Result.Success(to == StatusNames.Arrived
                ? $"{res.Patient.FullName} — gəldi. Növbə talonu: {res.QueueTicket}"
                : $"{res.Patient.FullName} — {StatusNames.Label(to)}", res.Id);
            result.Warning = Join(await _outbox.DispatchAddedAsync(ct));
            return (result, res.DoctorId);
        }

        public async Task<Result?> CancelAsync(int id, int? reasonId, CancelledBy? by, string? note, CancellationToken ct = default)
        {
            var res = await _db.Reserves.Include(r => r.Status).Include(r => r.Patient).Include(r => r.Payments)
                .FirstOrDefaultAsync(r => r.Id == id, ct);
            if (res == null) return null;
            if (!ReserveFlow.CanCancel(res))
                return Result.Failure($"«{StatusNames.Label(res.Status?.Name)}» statusunda rezervasiyanı ləğv etmək olmaz");

            CancellationReason? reason = null;
            if (reasonId != null)
                reason = await _db.CancellationReasons.FirstOrDefaultAsync(x => x.Id == reasonId && x.IsActive, ct);
            if (reason == null && await _db.CancellationReasons.AnyAsync(x => x.IsActive, ct))
                return Result.Failure("Ləğv səbəbini seçin");

            res.CancelDate = ClinicTime.Now;
            res.CancellationReasonId = reason?.Id;
            res.CancelledBy = by != null && Enum.IsDefined(by.Value) ? by : reason?.Party ?? CancelledBy.Patient;
            res.CancelReason = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            res.Status = await _db.ReserveStatuses.FirstAsync(rs => rs.Name == StatusNames.Cancelled, ct);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Rezervasiya ləğv edildi", AuditEntities.Reservation, res.Id,
                $"{res.Patient.FullName} — {reason?.Name ?? "səbəbsiz"} ({BookingLabels.Party(res.CancelledBy)})"
                + (res.CancelReason == null ? "" : $": {res.CancelReason}"));
            return Result.Success(res.Paid > 0
                ? $"Rezervasiya ləğv edildi. Xəstəyə {Fmt.Money(res.Paid)} qaytarılmalıdır — Kassada qeyd edin."
                : "Rezervasiya ləğv edildi", res.Id);
        }

        /// <summary>Examinations, birthday and the requested time; errors carry ReserveFormViewModel field names.</summary>
        private async Task<(List<ExaminationType> Exams, DateTime Start, SlotResult? Slot)> ValidateAsync(
            ReserveFormViewModel form, Reserve? existing, bool validateBirthday, Result r, CancellationToken ct)
        {
            var start = existing?.AppointmentAt ?? default;
            SlotResult? slot = null;

            var selectedIds = (form.ExaminationTypeIds ?? new()).Distinct().ToList();
            var examTypes = await _db.ExaminationTypes.Include(e => e.Equipment).Where(e => selectedIds.Contains(e.Id)).ToListAsync(ct);
            var shape = SlotService.Shape(examTypes);
            if (examTypes.Count == 0) r.Fail("Ən azı bir müayinə növü seçin", nameof(form.ExaminationTypeIds));

            if (validateBirthday && form.Birthday.HasValue)
            {
                if (form.Birthday.Value.Date > ClinicTime.Today) r.Fail("Doğum tarixi gələcəkdə ola bilməz", nameof(form.Birthday));
                else if (form.Birthday.Value.Year < 1900) r.Fail("Doğum tarixi düzgün deyil", nameof(form.Birthday));
            }

            if (form.DoctorId != null && form.AppointmentDate.HasValue && !string.IsNullOrEmpty(form.AppointmentTime))
            {
                var requested = form.AppointmentDate.Value.Date + (TimeSpan.TryParse(form.AppointmentTime, out var t) ? t : TimeSpan.Zero);
                // An unchanged appointment stays valid even if it is in the past or off the grid —
                // as long as its services still fit in the time it holds and need the same device.
                var unchanged = existing != null && form.DoctorId == existing.DoctorId && requested == TruncateToMinute(existing.AppointmentAt)
                    && shape.DurationMinutes <= existing.DurationMinutes && shape.EquipmentId == existing.EquipmentId;
                if (!unchanged)
                {
                    var error = _slots.Validate(form.DoctorId.Value, form.AppointmentDate.Value, form.AppointmentTime, existing?.Id, out start, out var s, shape);
                    slot = s;
                    if (error != null) r.Fail(error, nameof(form.AppointmentTime));
                }
            }
            return (examTypes, start, slot);
        }

        private static string? Join(List<string> warnings) => warnings.Count == 0 ? null : string.Join(" ", warnings);

        private static DateTime TruncateToMinute(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Kind);
    }
}
