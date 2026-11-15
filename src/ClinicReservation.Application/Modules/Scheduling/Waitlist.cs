using System.ComponentModel.DataAnnotations;
using ClinicReservation.Data;
using ClinicReservation.Events;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    public class WaitlistFormViewModel
    {
        [Required(ErrorMessage = "FİN kod daxil edilməlidir")]
        [RegularExpression(@"^\s*[A-Za-z0-9]{7}\s*$", ErrorMessage = "FİN kod 7 simvol olmalıdır (hərf və rəqəm)")]
        public string? PinCode { get; set; }
        // For a patient without a card yet.
        [StringLength(50)] public string? Name { get; set; }
        [StringLength(50)] public string? Surname { get; set; }
        [RegularExpression(@"^\+?[0-9\s\-()]{9,20}$", ErrorMessage = "Telefon nömrəsi düzgün deyil (məs: 050 123 45 67)")]
        public string? Phone { get; set; }
        public DateTime? Birthday { get; set; }

        public int? DoctorId { get; set; }
        public int? DepartmentId { get; set; }
        public int? ExaminationTypeId { get; set; }
        [Required(ErrorMessage = "Başlanğıc tarixi seçin")]
        public DateTime? FromDate { get; set; }
        [Required(ErrorMessage = "Son tarixi seçin")]
        public DateTime? ToDate { get; set; }
        /// <summary>"HH:mm", optional.</summary>
        public string? TimeFrom { get; set; }
        public string? TimeTo { get; set; }
        public bool NotifyBySms { get; set; } = true;
        [StringLength(300)] public string? Note { get; set; }

        public Patient? ExistingPatient { get; set; }
    }

    /// <summary>
    /// Gözləmə siyahısı: patients who want an earlier time. A cancelled visit is offered to the first
    /// matching patient (oldest entry first) whose service fits the freed time; reception then books it.
    /// </summary>
    public class Waitlist
    {
        /// <summary>Farthest a wish may reach, so the list doesn't fill with "any time this year".</summary>
        public const int MaxDays = 90;

        private readonly IAppDbContext _db;
        private readonly SlotService _slots;
        private readonly Outbox _outbox;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public Waitlist(IAppDbContext db, SlotService slots, Outbox outbox, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _slots = slots;
            _outbox = outbox;
            _audit = audit;
            _user = user;
        }

        public async Task<Result> AddAsync(WaitlistFormViewModel f, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var (field, m) in formErrors) r.Fail(m, field);
            var pin = Patient.NormalizePin(f.PinCode);
            var patient = string.IsNullOrEmpty(pin) ? null : await _db.Patients.FirstOrDefaultAsync(p => p.PinCode == pin, ct);
            if (patient == null && r.Ok)
            {
                if (string.IsNullOrWhiteSpace(f.Name)) r.Fail("Ad daxil edilməlidir", nameof(f.Name));
                if (string.IsNullOrWhiteSpace(f.Surname)) r.Fail("Soyad daxil edilməlidir", nameof(f.Surname));
                if (string.IsNullOrWhiteSpace(f.Phone)) r.Fail("Əlaqə nömrəsi daxil edilməlidir", nameof(f.Phone));
                if (f.Birthday == null) r.Fail("Doğum tarixi daxil edilməlidir", nameof(f.Birthday));
                else if (f.Birthday.Value.Date > ClinicTime.Today || f.Birthday.Value.Year < 1900) r.Fail("Doğum tarixi düzgün deyil", nameof(f.Birthday));
            }

            Doctor? doctor = null;
            if (f.DoctorId != null)
            {
                doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == f.DoctorId, ct);
                if (doctor == null) r.Fail("Həkim tapılmadı", nameof(f.DoctorId));
            }
            else if (f.DepartmentId == null) r.Fail("Həkim və ya şöbə seçin", nameof(f.DoctorId));
            else if (!await _db.Departments.AnyAsync(d => d.Id == f.DepartmentId, ct)) r.Fail("Şöbə tapılmadı", nameof(f.DepartmentId));
            if (f.ExaminationTypeId != null && !await _db.ExaminationTypes.AnyAsync(e => e.Id == f.ExaminationTypeId, ct))
                r.Fail("Müayinə növü tapılmadı", nameof(f.ExaminationTypeId));

            var today = ClinicTime.Today;
            if (f.FromDate != null && f.ToDate != null)
            {
                if (f.ToDate.Value.Date < f.FromDate.Value.Date) r.Fail("Son tarix başlanğıcdan əvvəl ola bilməz", nameof(f.ToDate));
                else if (f.ToDate.Value.Date < today) r.Fail("Son tarix keçmişdə ola bilməz", nameof(f.ToDate));
                else if (f.ToDate.Value.Date > today.AddDays(MaxDays)) r.Fail($"Ən çox {MaxDays} gün irəli yazmaq olar", nameof(f.ToDate));
            }
            var timeFrom = ParseTime(f.TimeFrom, nameof(f.TimeFrom), r);
            var timeTo = ParseTime(f.TimeTo, nameof(f.TimeTo), r);
            if (timeFrom != null && timeTo != null && timeTo <= timeFrom) r.Fail("Saat aralığı düzgün deyil", nameof(f.TimeTo));
            if (!r.Ok) return r;

            var created = patient == null;
            patient ??= _db.Patients.Add(new Patient
            {
                PinCode = pin, Name = f.Name!.Trim(), Surname = f.Surname!.Trim(), Phone = f.Phone!.Trim(),
                Birthday = f.Birthday!.Value.Date, CreatedAt = ClinicTime.Now
            }).Entity;

            var from = f.FromDate!.Value.Date < today ? today : f.FromDate.Value.Date;
            var dup = !created && await _db.WaitlistEntries.AnyAsync(w => w.PatientId == patient.Id
                && w.Status == WaitlistStatus.Waiting && w.DoctorId == f.DoctorId
                && (f.DoctorId != null || w.DepartmentId == f.DepartmentId) && w.ToDate >= today, ct);
            if (dup) return Result.Failure("Bu xəstə həmin həkim/şöbə üçün artıq gözləmə siyahısındadır");

            var entry = new WaitlistEntry
            {
                Patient = patient,
                DoctorId = doctor?.Id,
                DepartmentId = doctor?.DepartmentId ?? f.DepartmentId,
                ExaminationTypeId = f.ExaminationTypeId,
                FromDate = from,
                ToDate = f.ToDate!.Value.Date,
                TimeFrom = timeFrom,
                TimeTo = timeTo,
                NotifyBySms = f.NotifyBySms,
                Note = string.IsNullOrWhiteSpace(f.Note) ? null : f.Note.Trim(),
                CreatedAt = ClinicTime.Now,
                CreatedBy = _user.UserName
            };
            _db.WaitlistEntries.Add(entry);
            await _db.SaveChangesAsync(ct);
            if (created) _audit.Log("Xəstə kartı yaradıldı", AuditEntities.Patient, patient.Id, $"{patient.FullName} ({patient.PinCode})");
            _audit.Log("Gözləmə siyahısına yazıldı", AuditEntities.Waitlist, entry.Id,
                $"{patient.FullName} — {(doctor != null ? doctor.FullName : "şöbə üzrə")}, {Fmt.Date(entry.FromDate)}–{Fmt.Date(entry.ToDate)}");
            return Result.Success($"{patient.FullName} gözləmə siyahısına yazıldı", entry.Id);
        }

        /// <summary>
        /// A visit was cancelled: offer its time to the first waiting patient it suits. Returns the
        /// offered entry (null when nobody matches). Texts the patient through the outbox if they asked.
        /// </summary>
        public async Task<WaitlistEntry?> OfferFreedAsync(Reserve freed, int? skipEntryId = null, CancellationToken ct = default)
        {
            if (freed.DoctorId == null || freed.AppointmentAt <= ClinicTime.Now) return null;
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == freed.DoctorId, ct);
            if (doctor == null) return null;
            var day = freed.AppointmentAt.Date;
            var candidates = await _db.WaitlistEntries.Include(w => w.Patient).Include(w => w.ExaminationType).ThenInclude(e => e!.Equipment)
                .Where(w => w.Status == WaitlistStatus.Waiting && w.FromDate <= day && w.ToDate >= day
                            && w.PatientId != freed.PatientId && w.Id != skipEntryId
                            && (w.DoctorId == doctor.Id || (w.DoctorId == null && w.DepartmentId != null && w.DepartmentId == doctor.DepartmentId)))
                .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
                .ToListAsync(ct);

            var time = freed.AppointmentAt.ToString("HH:mm");
            foreach (var w in candidates.Where(w => w.Fits(freed.AppointmentAt)))
            {
                // The entry's own service must fit the freed time (length, device, doctor still free).
                var shape = SlotService.Shape(w.ExaminationType == null ? Array.Empty<ExaminationType>() : new[] { w.ExaminationType });
                if (_slots.Validate(doctor.Id, day, time, null, out _, out _, shape) != null) continue;

                w.Status = WaitlistStatus.Offered;
                w.OfferedAt = ClinicTime.Now;
                w.OfferedSlot = freed.AppointmentAt;
                w.OfferedDoctorId = doctor.Id;
                if (w.NotifyBySms) _outbox.Add(new WaitlistOffered(w.Id, w.PatientId, freed.AppointmentAt, doctor.FullName, _user.UserName));
                await _db.SaveChangesAsync(ct);
                _audit.Log("Gözləyən xəstəyə boş vaxt təklif edildi", AuditEntities.Waitlist, w.Id,
                    $"{w.Patient.FullName} — {Fmt.DateTime(freed.AppointmentAt)}, {doctor.FullName}" + (w.NotifyBySms ? ", SMS" : ""));
                await _outbox.DispatchAddedAsync(ct);
                return w;
            }
            return null;
        }

        /// <summary>The patient didn't want the offered time: back to waiting, and the time goes to the next one.</summary>
        public async Task<Result?> DeclineAsync(int id, CancellationToken ct = default)
        {
            var w = await _db.WaitlistEntries.Include(x => x.Patient).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w == null) return null;
            if (w.Status != WaitlistStatus.Offered) return Result.Failure("Bu qeyddə təklif yoxdur");
            var slot = w.OfferedSlot;
            var doctorId = w.OfferedDoctorId;
            w.Status = WaitlistStatus.Waiting;
            w.OfferedAt = null;
            w.OfferedSlot = null;
            w.OfferedDoctorId = null;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Gözləyən xəstə təklifdən imtina etdi", AuditEntities.Waitlist, w.Id, $"{w.Patient.FullName} — {Fmt.DateTime(slot)}");

            var message = $"{w.Patient.FullName} yenidən gözləyir";
            if (slot != null && doctorId != null)
            {
                var next = await OfferFreedAsync(new Reserve { DoctorId = doctorId, AppointmentAt = slot.Value, PatientId = w.PatientId }, w.Id, ct);
                if (next != null) message += $". Vaxt növbəti xəstəyə təklif edildi: {next.Patient.FullName}";
            }
            return Result.Success(message, w.Id);
        }

        public async Task<Result?> CloseAsync(int id, string? note, CancellationToken ct = default)
        {
            var w = await _db.WaitlistEntries.Include(x => x.Patient).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w == null) return null;
            if (w.Status is WaitlistStatus.Booked or WaitlistStatus.Closed) return Result.Failure("Qeyd artıq bağlıdır");
            w.Status = WaitlistStatus.Closed;
            w.ClosedAt = ClinicTime.Now;
            w.CloseNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            await _db.SaveChangesAsync(ct);
            _audit.Log("Gözləmə qeydi bağlandı", AuditEntities.Waitlist, w.Id, w.Patient.FullName + (w.CloseNote == null ? "" : $": {w.CloseNote}"));
            return Result.Success("Qeyd bağlandı", w.Id);
        }

        /// <summary>
        /// A new booking for the patient settles their open wishes for that doctor/department in its
        /// date range (and the entry it was booked from, if any). Saved by the caller.
        /// </summary>
        public async Task<int> SettleAsync(Reserve reserve, int? fromEntryId, CancellationToken ct = default)
        {
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == reserve.DoctorId, ct);
            var day = reserve.AppointmentAt.Date;
            var open = await _db.WaitlistEntries
                .Where(w => w.PatientId == reserve.PatientId && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
                .ToListAsync(ct);
            var hits = open.Where(w => w.Id == fromEntryId
                || (w.FromDate <= day && w.ToDate >= day
                    && (w.DoctorId == reserve.DoctorId || (w.DoctorId == null && doctor != null && w.DepartmentId == doctor.DepartmentId))))
                .ToList();
            foreach (var w in hits)
            {
                w.Status = WaitlistStatus.Booked;
                w.ReserveId = reserve.Id;
                w.ClosedAt = ClinicTime.Now;
            }
            return hits.Count;
        }

        private static TimeSpan? ParseTime(string? s, string field, Result r)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (TimeSpan.TryParseExact(s.Trim(), @"hh\:mm", null, out var t)) return t;
            r.Fail("Saat düzgün deyil (SS:dd)", field);
            return null;
        }
    }

    public class WaitlistQueries
    {
        private readonly IAppDbContext _db;
        public WaitlistQueries(IAppDbContext db) => _db = db;

        /// <summary>Offers first (they need a call), then waiting by age; closed ones only when asked.</summary>
        public async Task<List<WaitlistEntry>> ListAsync(WaitlistStatus? status, int? doctorId, int? departmentId, CancellationToken ct = default)
        {
            var q = _db.WaitlistEntries.AsNoTracking()
                .Include(w => w.Patient).Include(w => w.Doctor).Include(w => w.Department)
                .Include(w => w.ExaminationType).Include(w => w.OfferedDoctor).Include(w => w.Reserve)
                .AsQueryable();
            q = status != null ? q.Where(w => w.Status == status)
                : q.Where(w => w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered);
            if (doctorId != null) q = q.Where(w => w.DoctorId == doctorId || w.OfferedDoctorId == doctorId);
            if (departmentId != null) q = q.Where(w => w.DepartmentId == departmentId);
            return await q.OrderBy(w => w.Status == WaitlistStatus.Offered ? 0 : 1).ThenBy(w => w.CreatedAt).Take(500).ToListAsync(ct);
        }

        public Task<WaitlistEntry?> EntryAsync(int id, CancellationToken ct = default) =>
            _db.WaitlistEntries.Include(w => w.Patient).FirstOrDefaultAsync(w => w.Id == id, ct);

        public Task<Patient?> PatientByPinAsync(string? pin, CancellationToken ct = default)
        {
            var p = Patient.NormalizePin(pin);
            return p.Length == 0 ? Task.FromResult<Patient?>(null) : _db.Patients.AsNoTracking().FirstOrDefaultAsync(x => x.PinCode == p, ct);
        }

        public Task<int> OfferedCountAsync(CancellationToken ct = default) =>
            _db.WaitlistEntries.CountAsync(w => w.Status == WaitlistStatus.Offered, ct);

        public async Task<(List<Doctor> Doctors, List<Department> Departments, List<ExaminationType> Exams)> ListsAsync(CancellationToken ct = default) =>
            (await _db.Doctors.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct),
             await _db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct),
             await _db.ExaminationTypes.AsNoTracking().OrderBy(e => e.Name).ToListAsync(ct));
    }
}
