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
        private readonly Waitlist _waitlist;
        private readonly NoShows _noShows;
        private readonly Referrals _referrals;
        private readonly FollowUps _followUps;
        private readonly PriceQueries _prices;

        public Reservations(IAppDbContext db, SlotService slots, QueueService queue, Outbox outbox, AuditService audit, ICurrentUser user,
            Waitlist waitlist, NoShows noShows, Referrals referrals, FollowUps followUps, PriceQueries prices)
        {
            _followUps = followUps;
            _prices = prices;
            _referrals = referrals;
            _noShows = noShows;
            _waitlist = waitlist;
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

            var (createdPatient, p) = PatientFor(form, patient);
            patient = p;
            var quote = await _prices.QuoteAsync(createdPatient ? null : patient.Id, form.DoctorId, start, examTypes, null, ct);

            var reserve = new Reserve
            {
                Patient = patient,
                DoctorId = form.DoctorId,
                RoomId = slot!.RoomId,
                AppointmentAt = start,
                DurationMinutes = slot.SlotMinutes,
                EquipmentId = SlotService.Shape(examTypes).EquipmentId,
                Source = Enum.IsDefined(form.Source) ? form.Source : ReserveSource.Reception,
                Overbooked = slot.Overbooked,
                FollowUpOfId = quote.FollowUpOfId,
                AddedDate = ClinicTime.Now,
                Status = await _db.ReserveStatuses.FirstAsync(rs => rs.Name == StatusNames.Waiting, ct),
                ReserveExaminationTypes = quote.Lines.Select(l => l.ToLine()).ToList()
            };
            _db.Reserves.Add(reserve);
            await _db.SaveChangesAsync(ct);
            var settled = await _waitlist.SettleAsync(reserve, form.WaitlistId, ct) > 0;
            settled |= await _followUps.SettleAsync(reserve, form.FollowUpId, ct);
            var referral = await _referrals.SettleAsync(reserve, form.ReferralId, examTypes.Select(e => e.Id).ToList(), ct);
            if (referral != null && form.ReferralId == referral.Id) reserve.Source = ReserveSource.Referral;
            if (settled || referral != null) await _db.SaveChangesAsync(ct);

            if (createdPatient)
                _audit.Log("Xəstə kartı yaradıldı", AuditEntities.Patient, patient.Id, $"{patient.FullName} ({patient.PinCode})");
            var doctorName = await _db.Doctors.Where(d => d.Id == reserve.DoctorId).Select(d => d.FullName).FirstOrDefaultAsync(ct);
            _audit.Log("Rezervasiya yaradıldı", AuditEntities.Reservation, reserve.Id,
                $"{patient.FullName} — {Fmt.DateTime(reserve.AppointmentAt)}, {doctorName}" + (reserve.Overbooked ? " (əlavə qəbul, araya salındı)" : ""));
            var ok = Result.Success($"{patient.FullName} — {Fmt.DateTime(reserve.AppointmentAt)} üçün rezervasiya yaradıldı." + quote.Note, reserve.Id);
            if (!createdPatient) ok.Warning = _noShows.Warning(await _noShows.CountAsync(patient.Id, ct));
            return ok;
        }

        /// <summary>The existing card (phone refreshed), or a new one from the form.</summary>
        private (bool Created, Patient Patient) PatientFor(ReserveFormViewModel form, Patient? patient)
        {
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
                return (true, patient);
            }
            if (!string.IsNullOrWhiteSpace(form.Phone) && form.Phone.Trim() != patient.Phone)
                patient.Phone = form.Phone.Trim();
            return (false, patient);
        }

        // ---------- Course (kurs) ----------

        public const int MaxSeriesSpanDays = 365;

        /// <summary>
        /// Dates of a course: from the start date, on the chosen weekdays, until the count is reached. Each date
        /// gets the asked time if free, otherwise the nearest free time that day, otherwise nothing.
        /// </summary>
        public async Task<(Result Result, List<SeriesPlanItem> Plan)> PlanSeriesAsync(ReserveFormViewModel form, Patient? patient,
            IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var (f, m) in formErrors) r.Fail(m, f);
            var (examTypes, _, _) = await ValidateAsync(form, null, validateBirthday: patient == null, r, ct, checkSlot: false);
            if (form.SeriesCount < 2) r.Fail("Kurs üçün ən azı 2 seans", nameof(form.SeriesCount));
            if (!TimeSpan.TryParse(form.AppointmentTime, out var time)) r.Fail("Seansların saatını seçin", nameof(form.AppointmentTime));
            if (!r.Ok) return (r, new());

            var shape = SlotService.Shape(examTypes);
            var days = (form.SeriesDays is { Count: > 0 } ? form.SeriesDays : new List<DayOfWeek> { form.AppointmentDate!.Value.DayOfWeek }).ToHashSet();
            var asked = time.ToString(@"hh\:mm");
            var plan = new List<SeriesPlanItem>();
            var start = form.AppointmentDate!.Value.Date;
            for (var d = start; plan.Count < form.SeriesCount && d < start.AddDays(MaxSeriesSpanDays); d = d.AddDays(1))
            {
                if (!days.Contains(d.DayOfWeek)) continue;
                var slots = _slots.GetSlots(form.DoctorId!.Value, d, null, shape.DurationMinutes, shape.EquipmentId);
                var mine = slots.Slots.FirstOrDefault(x => x.Time == asked);
                if (mine is { Available: true }) { plan.Add(new(d, asked, SeriesPlanStatus.Ok, null)); continue; }
                var near = slots.Slots.Where(x => x.Available)
                    .OrderBy(x => Math.Abs((TimeSpan.Parse(x.Time) - time).TotalMinutes)).FirstOrDefault();
                plan.Add(near != null
                    ? new(d, near.Time, SeriesPlanStatus.Alternative, $"{asked} doludur")
                    : new(d, null, SeriesPlanStatus.NoTime, slots.Message ?? "Boş saat yoxdur"));
            }
            if (plan.Count < form.SeriesCount) r.Fail($"Bir il ərzində yalnız {plan.Count} uyğun gün tapıldı");
            return (r, plan);
        }

        /// <summary>Books the course as planned now (dates without a free time are skipped and reported).</summary>
        public async Task<Result> CreateSeriesAsync(ReserveFormViewModel form, Patient? patient, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var (r, plan) = await PlanSeriesAsync(form, patient, formErrors, ct);
            if (!r.Ok) return r;
            var bookable = plan.Where(x => x.Time != null).ToList();
            if (bookable.Count == 0) return Result.Failure("Heç bir seans üçün boş saat tapılmadı");

            var examTypes = await _db.ExaminationTypes.Include(e => e.Equipment).Where(e => form.ExaminationTypeIds!.Contains(e.Id)).ToListAsync(ct);
            var shape = SlotService.Shape(examTypes);
            var (createdPatient, p) = PatientFor(form, patient);
            var days = form.SeriesDays is { Count: > 0 } ? form.SeriesDays : new List<DayOfWeek> { form.AppointmentDate!.Value.DayOfWeek };
            var series = new ReserveSeries
            {
                Patient = p,
                DoctorId = form.DoctorId,
                Count = bookable.Count,
                Pattern = $"{form.SeriesCount} seans · {string.Join(", ", days.OrderBy(d => ((int)d + 6) % 7).Select(Fmt.DayShortName))} · {form.AppointmentTime}",
                CreatedAt = ClinicTime.Now,
                CreatedBy = _user.UserName
            };
            var waiting = await _db.ReserveStatuses.FirstAsync(rs => rs.Name == StatusNames.Waiting, ct);
            var no = 0;
            foreach (var item in bookable)
            {
                // Re-checked at booking time: someone may have taken the time since the plan was shown.
                var err = _slots.Validate(form.DoctorId!.Value, item.Date, item.Time!, null, out var at, out var slot, shape);
                if (err != null) continue;
                series.Reserves.Add(new Reserve
                {
                    Patient = p, DoctorId = form.DoctorId, RoomId = slot.RoomId, AppointmentAt = at, DurationMinutes = slot.SlotMinutes,
                    EquipmentId = shape.EquipmentId, Source = Enum.IsDefined(form.Source) ? form.Source : ReserveSource.Reception,
                    AddedDate = ClinicTime.Now, Status = waiting, SeriesNo = ++no,
                    ReserveExaminationTypes = (await _prices.QuoteAsync(createdPatient ? null : p.Id, form.DoctorId, at, examTypes, null, ct)).Lines.Select(l => l.ToLine()).ToList()
                });
            }
            if (series.Reserves.Count == 0) return Result.Failure("Seçilən vaxtlar artıq doludur — tarixləri yenidən yoxlayın");
            series.Count = series.Reserves.Count;
            _db.ReserveSeries.Add(series);
            await _db.SaveChangesAsync(ct);

            if (createdPatient)
                _audit.Log("Xəstə kartı yaradıldı", AuditEntities.Patient, p.Id, $"{p.FullName} ({p.PinCode})");
            var first = series.Reserves.OrderBy(x => x.AppointmentAt).First();
            var settledW = await _waitlist.SettleAsync(first, form.WaitlistId, ct) > 0;
            var settledR = await _referrals.SettleAsync(first, form.ReferralId, examTypes.Select(e => e.Id).ToList(), ct) != null;
            var settledF = await _followUps.SettleAsync(first, form.FollowUpId, ct);
            if (settledW || settledR || settledF) await _db.SaveChangesAsync(ct);
            _audit.Log("Kurs yaradıldı", AuditEntities.Reservation, first.Id,
                $"{p.FullName}: {series.Pattern}, {series.Count} seans ({Fmt.Date(first.AppointmentAt)} – {Fmt.Date(series.Reserves.Max(x => x.AppointmentAt))})");
            var skipped = form.SeriesCount - series.Count;
            var warning = createdPatient ? null : _noShows.Warning(await _noShows.CountAsync(p.Id, ct));
            var done = Result.Success($"{p.FullName} üçün {series.Count} seanslıq kurs yaradıldı" + (skipped > 0 ? $" ({skipped} gün boş saat olmadığı üçün buraxıldı)" : ""), first.Id);
            done.Warning = warning;
            return done;
        }

        /// <summary>Cancels this session and the later ones of its course that can still be cancelled.</summary>
        public async Task<Result?> CancelSeriesFromAsync(int reserveId, int? reasonId, CancelledBy? by, string? note, CancellationToken ct = default)
        {
            var res = await _db.Reserves.FirstOrDefaultAsync(r => r.Id == reserveId, ct);
            if (res == null) return null;
            if (res.SeriesId == null) return await CancelAsync(reserveId, reasonId, by, note, ct);
            var ids = await _db.Reserves.Include(r => r.Status)
                .Where(r => r.SeriesId == res.SeriesId && r.AppointmentAt >= res.AppointmentAt)
                .OrderBy(r => r.AppointmentAt).ToListAsync(ct);
            var done = 0;
            foreach (var x in ids.Where(ReserveFlow.CanCancel))
                if ((await CancelAsync(x.Id, reasonId, by, note, ct))?.Ok == true) done++;
            return done == 0 ? Result.Failure("Ləğv ediləcək seans yoxdur") : Result.Success($"Kursun {done} seansı ləğv edildi", reserveId);
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
                reserve.Overbooked = slot.Overbooked;
            }

            var selectedIds = examTypes.Select(e => e.Id).ToHashSet();
            var items = reserve.ReserveExaminationTypes!;
            var removed = items.Where(i => !selectedIds.Contains(i.ExaminationTypeId)).ToList();
            _db.ReserveExaminationTypes.RemoveRange(removed);
            var existingIds = items.Select(i => i.ExaminationTypeId).ToHashSet();
            var added = examTypes.Where(e => !existingIds.Contains(e.Id)).ToList();
            if (added.Count > 0)
                foreach (var line in (await _prices.QuoteAsync(reserve.PatientId, reserve.DoctorId, reserve.AppointmentAt, added, reserve.Id, ct)).Lines)
                    items.Add(line.ToLine());
            if (removed.Count > 0) changes.Add("çıxarıldı: " + string.Join(", ", removed.Select(x => x.ExaminationType.Name)));
            if (added.Count > 0) changes.Add("əlavə edildi: " + string.Join(", ", added.Select(a => a.Name)));

            await _db.SaveChangesAsync(ct);
            _audit.Log("Rezervasiya dəyişdirildi", AuditEntities.Reservation, reserve.Id,
                $"{reserve.Patient.FullName}: " + (changes.Count > 0 ? string.Join("; ", changes) : "dəyişiklik yoxdur"));
            return Result.Success("Rezervasiya yeniləndi", reserve.Id);
        }

        /// <summary>
        /// Calendar drag-and-drop: another time and/or doctor, services unchanged. The new time must be free for
        /// the whole visit (doctor, room, device); an extra (overbooked) visit becomes a normal one. Null = not found.
        /// </summary>
        public async Task<Result?> MoveAsync(int id, int doctorId, DateTime date, string time, CancellationToken ct = default)
        {
            var res = await _db.Reserves.Include(r => r.Status).Include(r => r.Patient)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType).ThenInclude(e => e.Equipment)
                .FirstOrDefaultAsync(r => r.Id == id, ct);
            if (res == null) return null;
            if (!ReserveFlow.CanReschedule(res))
                return Result.Failure($"«{StatusNames.Label(res.Status?.Name)}» statusunda rezervasiyanın vaxtı dəyişmir");
            if (res.AppointmentAt <= ClinicTime.Now) return Result.Failure("Keçmiş rezervasiyanı köçürmək olmaz");

            var shape = SlotService.Shape(res.ReserveExaminationTypes!.Select(x => x.ExaminationType));
            var error = _slots.Validate(doctorId, date, time, res.Id, out var start, out var slot, shape);
            if (error != null) return Result.Failure(error);
            if (start == res.AppointmentAt && doctorId == res.DoctorId) return Result.Success("Dəyişiklik yoxdur", res.Id);

            var from = res.AppointmentAt;
            var doctorChanged = doctorId != res.DoctorId;
            res.AppointmentAt = start;
            res.DoctorId = doctorId;
            res.RoomId = slot.RoomId;
            res.DurationMinutes = slot.SlotMinutes;
            res.EquipmentId = shape.EquipmentId;
            res.Overbooked = false;
            await _db.SaveChangesAsync(ct);
            var doctorName = await _db.Doctors.Where(d => d.Id == doctorId).Select(d => d.FullName).FirstAsync(ct);
            _audit.Log("Rezervasiya köçürüldü", AuditEntities.Reservation, res.Id,
                $"{res.Patient.FullName}: {Fmt.DateTime(from)} → {Fmt.DateTime(start)}" + (doctorChanged ? $", həkim: {doctorName}" : ""));
            return Result.Success($"{res.Patient.FullName} — {Fmt.DateTime(start)}" + (doctorChanged ? $", {doctorName}" : "") + " vaxtına köçürüldü", res.Id);
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
                case StatusNames.NoShow:
                    res.NoShowAt = now;
                    res.NoShowAuto = false;
                    break;
                case StatusNames.Arrived:
                    // Late after a «Gəlmədi» mark: the visit counts as attended.
                    res.NoShowAt = null;
                    res.NoShowAuto = false;
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
            var offered = await _waitlist.OfferFreedAsync(res, ct: ct);
            var result = Result.Success(res.Paid > 0
                ? $"Rezervasiya ləğv edildi. Xəstəyə {Fmt.Money(res.Paid)} qaytarılmalıdır — Kassada qeyd edin."
                : "Rezervasiya ləğv edildi", res.Id);
            if (offered != null)
                result.Message = result.Message!.TrimEnd('.') + $". Boşalan vaxt gözləmə siyahısından {offered.Patient.FullName} üçün təklif edildi"
                    + (offered.NotifyBySms ? " (SMS göndərildi)." : " — xəstəyə zəng edin.");
            return result;
        }

        /// <summary>Examinations, birthday and the requested time; errors carry ReserveFormViewModel field names.</summary>
        private async Task<(List<ExaminationType> Exams, DateTime Start, SlotResult? Slot)> ValidateAsync(
            ReserveFormViewModel form, Reserve? existing, bool validateBirthday, Result r, CancellationToken ct, bool checkSlot = true)
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

            if (checkSlot && form.DoctorId != null && form.AppointmentDate.HasValue && !string.IsNullOrEmpty(form.AppointmentTime))
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
                    // Busy, but reception chose «araya sal»: allowed with the permission, within the doctor's limit.
                    if (error != null && form.Overbook)
                    {
                        if (!_user.Can(Permissions.ReservationsOverbook)) error = "Əlavə qəbul (araya salmaq) üçün icazəniz yoxdur";
                        else
                        {
                            error = _slots.ValidateOverbook(form.DoctorId.Value, form.AppointmentDate.Value, form.AppointmentTime, existing?.Id, out start, out s, shape);
                            slot = s;
                        }
                    }
                    if (error != null) r.Fail(error, nameof(form.AppointmentTime));
                }
            }
            return (examTypes, start, slot);
        }

        private static string? Join(List<string> warnings) => warnings.Count == 0 ? null : string.Join(" ", warnings);

        private static DateTime TruncateToMinute(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Kind);
    }
}
