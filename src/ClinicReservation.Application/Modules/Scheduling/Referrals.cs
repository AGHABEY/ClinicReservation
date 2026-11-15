using System.ComponentModel.DataAnnotations;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    public class ReferralFormViewModel
    {
        /// <summary>The visit it is written in (doctor's referral); null = outside referral entered by reception.</summary>
        public int? FromReserveId { get; set; }

        // Outside referral: who the patient is and who sent them.
        public string? PinCode { get; set; }
        [StringLength(200, ErrorMessage = "Ən çox 200 simvol")]
        public string? ExternalFrom { get; set; }

        public int? ToDepartmentId { get; set; }
        public int? ToDoctorId { get; set; }
        public int? ExaminationTypeId { get; set; }
        public ReferralPriority Priority { get; set; } = ReferralPriority.Routine;

        [StringLength(10, ErrorMessage = "Kod ən çox 10 simvol ola bilər")]
        [RegularExpression(@"^\s*[A-Za-z][0-9]{2}(\.[0-9A-Za-z]{1,4})?\s*$", ErrorMessage = "BXT-10 kodu düzgün deyil (məs: I10)")]
        public string? IcdCode { get; set; }

        [StringLength(500, ErrorMessage = "Ən çox 500 simvol")]
        public string? Reason { get; set; }

        [Range(1, 365, ErrorMessage = "Etibarlılıq 1–365 gün olmalıdır")]
        public int ValidDays { get; set; } = 30;
    }

    /// <summary>Göndəriş use cases: write (in a visit) or register (from outside), book, cancel.</summary>
    public class Referrals
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public Referrals(IAppDbContext db, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _audit = audit;
            _user = user;
        }

        public async Task<Result> CreateAsync(ReferralFormViewModel f, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var (field, m) in formErrors) r.Fail(m, field);

            Reserve? visit = null;
            Patient? patient;
            if (f.FromReserveId != null)
            {
                visit = await _db.Reserves.Include(x => x.Patient).Include(x => x.Status).FirstOrDefaultAsync(x => x.Id == f.FromReserveId, ct);
                if (visit == null) return Result.Failure("Vizit tapılmadı");
                if (visit.Status?.Name is not (StatusNames.Arrived or StatusNames.InProgress or StatusNames.Completed))
                    return Result.Failure("Göndəriş yalnız qəbulda olan və ya tamamlanmış vizitdən yazılır");
                patient = visit.Patient;
            }
            else
            {
                var pin = Patient.NormalizePin(f.PinCode);
                patient = pin.Length == 0 ? null : await _db.Patients.FirstOrDefaultAsync(p => p.PinCode == pin, ct);
                if (patient == null) r.Fail("Bu FİN ilə xəstə kartı tapılmadı — əvvəlcə kart açın", nameof(f.PinCode));
                if (string.IsNullOrWhiteSpace(f.ExternalFrom)) r.Fail("Göndərən müəssisəni / həkimi yazın", nameof(f.ExternalFrom));
            }

            if (f.ToDepartmentId == null && f.ToDoctorId == null && f.ExaminationTypeId == null)
                r.Fail("Hara göndərilir: şöbə, həkim və ya xidmət seçin", nameof(f.ToDepartmentId));
            Doctor? toDoctor = null;
            if (f.ToDoctorId != null && (toDoctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == f.ToDoctorId && d.IsActive, ct)) == null)
                r.Fail("Həkim tapılmadı", nameof(f.ToDoctorId));
            var deptId = f.ToDepartmentId ?? toDoctor?.DepartmentId;
            var dept = deptId == null ? null : await _db.Departments.FirstOrDefaultAsync(d => d.Id == deptId, ct);
            if (f.ToDepartmentId != null && dept == null) r.Fail("Şöbə tapılmadı", nameof(f.ToDepartmentId));
            var exam = f.ExaminationTypeId == null ? null : await _db.ExaminationTypes.FirstOrDefaultAsync(e => e.Id == f.ExaminationTypeId, ct);
            if (f.ExaminationTypeId != null && exam == null) r.Fail("Xidmət tapılmadı", nameof(f.ExaminationTypeId));
            if (string.IsNullOrWhiteSpace(f.Reason)) r.Fail("Göndərişin səbəbini yazın", nameof(f.Reason));
            if (toDoctor != null && visit != null && toDoctor.Id == visit.DoctorId) r.Fail("Xəstəni öz qəbulunuza göndərmək olmaz — kontrol qəbulu təyin edin", nameof(f.ToDoctorId));
            if (!r.Ok) return r;

            var now = ClinicTime.Now;
            var referral = new Referral
            {
                PatientId = patient!.Id,
                FromReserveId = visit?.Id,
                FromDoctorId = visit?.DoctorId,
                ExternalFrom = visit == null ? f.ExternalFrom!.Trim() : null,
                ToDepartment = dept,
                ToDoctor = toDoctor,
                ExaminationType = exam,
                Priority = Enum.IsDefined(f.Priority) ? f.Priority : ReferralPriority.Routine,
                IcdCode = string.IsNullOrWhiteSpace(f.IcdCode) ? null : f.IcdCode.Trim().ToUpperInvariant(),
                Reason = f.Reason!.Trim(),
                CreatedAt = now,
                CreatedBy = _user.UserName,
                ValidUntil = now.Date.AddDays(f.ValidDays)
            };
            _db.Referrals.Add(referral);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Göndəriş yazıldı", AuditEntities.Referral, referral.Id,
                $"{patient.FullName} → {ReferralFlow.Target(referral)}" + (referral.Priority == ReferralPriority.Urgent ? " (təcili)" : "")
                + (referral.ExternalFrom != null ? $", göndərən: {referral.ExternalFrom}" : ""));
            return Result.Success($"Göndəriş yazıldı: {ReferralFlow.Target(referral)}", referral.Id);
        }

        public async Task<Result?> CancelAsync(int id, string? reason, CancellationToken ct = default)
        {
            var x = await _db.Referrals.Include(r => r.Patient).FirstOrDefaultAsync(r => r.Id == id, ct);
            if (x == null) return null;
            if (x.Status == ReferralStatus.Cancelled) return Result.Failure("Göndəriş artıq ləğv edilib");
            if (string.IsNullOrWhiteSpace(reason)) return Result.Failure("Ləğv səbəbini yazın");
            x.Status = ReferralStatus.Cancelled;
            x.CancelledAt = ClinicTime.Now;
            x.CancelReason = reason.Trim();
            await _db.SaveChangesAsync(ct);
            _audit.Log("Göndəriş ləğv edildi", AuditEntities.Referral, x.Id, $"{x.Patient.FullName}: {x.CancelReason}");
            return Result.Success("Göndəriş ləğv edildi", x.Id);
        }

        /// <summary>
        /// A new booking carries out the patient's waiting referral it was booked from, or one that matches its doctor /
        /// department / service. Saved by the caller. Returns the referral linked (if any).
        /// </summary>
        public async Task<Referral?> SettleAsync(Reserve reserve, int? fromReferralId, IReadOnlyCollection<int> examIds, CancellationToken ct = default)
        {
            var doctor = await _db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == reserve.DoctorId, ct);
            var today = ClinicTime.Today;
            var candidates = await _db.Referrals.Include(r => r.Reserve).ThenInclude(v => v!.Status)
                .Where(r => r.PatientId == reserve.PatientId && r.Status != ReferralStatus.Cancelled && r.ValidUntil >= today)
                .OrderBy(r => r.CreatedAt).ToListAsync(ct);
            var open = candidates.Where(r => ReferralFlow.State(r, today) == ReferralState.Waiting).ToList();
            var hit = open.FirstOrDefault(r => r.Id == fromReferralId)
                      ?? open.FirstOrDefault(r => (r.ToDoctorId != null && r.ToDoctorId == reserve.DoctorId)
                                                  || (r.ExaminationTypeId != null && examIds.Contains(r.ExaminationTypeId.Value))
                                                  || (r.ToDoctorId == null && r.ExaminationTypeId == null && r.ToDepartmentId != null && r.ToDepartmentId == doctor?.DepartmentId));
            if (hit == null) return null;
            hit.Status = ReferralStatus.Booked;
            hit.ReserveId = reserve.Id;
            hit.BookedAt = ClinicTime.Now;
            return hit;
        }
    }

    public class ReferralQueries
    {
        private readonly IAppDbContext _db;
        public ReferralQueries(IAppDbContext db) => _db = db;

        private IQueryable<Referral> Full => _db.Referrals.AsNoTracking()
            .Include(r => r.Patient).Include(r => r.FromDoctor).Include(r => r.ToDoctor).Include(r => r.ToDepartment)
            .Include(r => r.ExaminationType).Include(r => r.Reserve).ThenInclude(v => v!.Status);

        /// <summary>For reception; urgent first, then oldest. State filter is applied after loading (it depends on the visit).</summary>
        public async Task<List<Referral>> ListAsync(ReferralState? state, int? departmentId, string? q, CancellationToken ct = default)
        {
            var query = Full;
            if (departmentId != null) query = query.Where(r => r.ToDepartmentId == departmentId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim().ToLower();
                query = query.Where(r => r.Patient.Name.ToLower().Contains(t) || r.Patient.Surname.ToLower().Contains(t) || r.Patient.PinCode.ToLower().Contains(t));
            }
            // "Waiting" (the default) needs only live ones; the others look back three months.
            var since = ClinicTime.Today.AddMonths(-3);
            var list = await query.Where(r => r.CreatedAt >= since || r.ValidUntil >= ClinicTime.Today)
                .OrderByDescending(r => r.Priority).ThenBy(r => r.CreatedAt).Take(1000).ToListAsync(ct);
            var today = ClinicTime.Today;
            var s = state ?? ReferralState.Waiting;
            return list.Where(r => ReferralFlow.State(r, today) == s).Take(500).ToList();
        }

        public Task<Referral?> ReferralAsync(int id, CancellationToken ct = default) => Full.FirstOrDefaultAsync(r => r.Id == id, ct);

        public Task<List<Referral>> FromVisitAsync(int reserveId, CancellationToken ct = default) =>
            Full.Where(r => r.FromReserveId == reserveId).OrderBy(r => r.CreatedAt).ToListAsync(ct);

        /// <summary>The referral this visit was booked from (shown on the visit).</summary>
        public Task<Referral?> ForVisitAsync(int reserveId, CancellationToken ct = default) =>
            Full.Include(r => r.FromReserve).FirstOrDefaultAsync(r => r.ReserveId == reserveId, ct);

        public async Task<int> WaitingCountAsync(CancellationToken ct = default) => (await ListAsync(ReferralState.Waiting, null, null, ct)).Count;

        public async Task<(List<Department> Departments, List<Doctor> Doctors, List<ExaminationType> Exams)> FormListsAsync(CancellationToken ct = default) =>
            (await _db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct),
             await _db.Doctors.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct),
             await _db.ExaminationTypes.AsNoTracking().OrderBy(e => e.Name).ToListAsync(ct));
    }
}
