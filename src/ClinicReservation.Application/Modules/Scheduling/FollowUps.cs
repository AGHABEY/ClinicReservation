using ClinicReservation.Data;
using ClinicReservation.Events;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Scheduling
{
    public enum FollowUpTab { Due, Upcoming, Booked, Closed }

    /// <summary>Kontrol qəbulları: reminding, closing and settling the doctor's follow-up plans.</summary>
    public class FollowUps
    {
        private readonly IAppDbContext _db;
        private readonly Outbox _outbox;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;
        private readonly SchedulingOptions _options;

        public FollowUps(IAppDbContext db, Outbox outbox, AuditService audit, ICurrentUser user, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _outbox = outbox;
            _audit = audit;
            _user = user;
            _options = options.Value;
        }

        /// <summary>Texts the patient(s) about their follow-up. One id, or every due one not yet texted.</summary>
        public async Task<Result> RemindAsync(int? id, CancellationToken ct = default)
        {
            var until = ClinicTime.Today.AddDays(_options.FollowUpListAheadDays);
            var q = _db.FollowUps.Include(f => f.Patient).Include(f => f.Doctor).Where(f => f.Status == FollowUpStatus.Due);
            q = id != null ? q.Where(f => f.Id == id) : q.Where(f => f.SmsSentAt == null && f.DueDate <= until);
            var list = await q.ToListAsync(ct);
            if (list.Count == 0) return Result.Failure(id != null ? "Bu kontrol qəbulu artıq bağlıdır və ya yazılıb" : "SMS göndəriləcək xəstə yoxdur");

            var now = ClinicTime.Now;
            foreach (var f in list)
            {
                f.SmsSentAt = now;
                _outbox.Add(new FollowUpReminder(f.Id, f.PatientId, f.DueDate, f.Doctor?.FullName ?? "", _user.UserName));
            }
            await _db.SaveChangesAsync(ct);
            foreach (var f in list)
                _audit.Log("Kontrol qəbulu xatırladıldı (SMS)", AuditEntities.Reservation, f.FromReserveId, $"{f.Patient.FullName} — {Fmt.Date(f.DueDate)}");
            var warnings = await _outbox.DispatchAddedAsync(ct);
            var r = Result.Success(list.Count == 1 ? $"{list[0].Patient.FullName} — SMS göndərildi" : $"{list.Count} xəstəyə SMS göndərildi");
            if (warnings.Count > 0) r.Warning = string.Join(" ", warnings.Distinct());
            return r;
        }

        public async Task<Result?> CloseAsync(int id, string? note, CancellationToken ct = default)
        {
            var f = await _db.FollowUps.Include(x => x.Patient).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f == null) return null;
            if (f.Status != FollowUpStatus.Due) return Result.Failure("Bu kontrol qəbulu artıq bağlıdır və ya yazılıb");
            f.Status = FollowUpStatus.Closed;
            f.ClosedAt = ClinicTime.Now;
            f.CloseNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            await _db.SaveChangesAsync(ct);
            _audit.Log("Kontrol qəbulu bağlandı", AuditEntities.Reservation, f.FromReserveId, f.Patient.FullName + (f.CloseNote == null ? "" : $": {f.CloseNote}"));
            return Result.Success("Kontrol qəbulu siyahıdan çıxarıldı", f.Id);
        }

        /// <summary>A new visit with the doctor (after the one that asked for it) settles the plan. Saved by the caller.</summary>
        public async Task<bool> SettleAsync(Reserve reserve, int? fromFollowUpId, CancellationToken ct = default)
        {
            var plans = await _db.FollowUps.Include(f => f.FromReserve)
                .Where(f => f.PatientId == reserve.PatientId && f.Status == FollowUpStatus.Due)
                .ToListAsync(ct);
            var hit = plans.FirstOrDefault(f => f.Id == fromFollowUpId)
                      ?? plans.Where(f => (f.DoctorId == null || f.DoctorId == reserve.DoctorId) && f.FromReserve.AppointmentAt < reserve.AppointmentAt)
                              .OrderBy(f => f.DueDate).FirstOrDefault();
            if (hit == null) return false;
            hit.Status = FollowUpStatus.Booked;
            hit.ReserveId = reserve.Id;
            return true;
        }
    }

    /// <summary>The doctor's follow-up in the record becomes (or updates / withdraws) a plan.</summary>
    public sealed class PlanFollowUpOnRecord : Events.EventHandler<FollowUpPlanned>
    {
        private readonly IAppDbContext _db;
        public PlanFollowUpOnRecord(IAppDbContext db) => _db = db;

        protected override async Task<string?> HandleAsync(FollowUpPlanned e, CancellationToken ct)
        {
            var plan = await _db.FollowUps.FirstOrDefaultAsync(f => f.FromReserveId == e.ReserveId, ct);
            if (e.DueDate == null)
            {
                // Withdrawn by the doctor: forget a plan nobody acted on yet.
                if (plan is { Status: FollowUpStatus.Due }) _db.FollowUps.Remove(plan);
            }
            else if (plan == null)
            {
                _db.FollowUps.Add(new FollowUp
                {
                    FromReserveId = e.ReserveId, PatientId = e.PatientId, DoctorId = e.DoctorId,
                    DueDate = e.DueDate.Value.Date, CreatedAt = ClinicTime.Now
                });
            }
            else if (plan.Status == FollowUpStatus.Due && plan.DueDate != e.DueDate.Value.Date)
            {
                plan.DueDate = e.DueDate.Value.Date;
                plan.SmsSentAt = null; // a new date deserves a new reminder
            }
            await _db.SaveChangesAsync(ct);
            return null;
        }
    }

    public class FollowUpQueries
    {
        private readonly IAppDbContext _db;
        private readonly SchedulingOptions _options;

        public FollowUpQueries(IAppDbContext db, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public int AheadDays => _options.FollowUpListAheadDays;

        public async Task<List<FollowUp>> ListAsync(FollowUpTab tab, int? doctorId, CancellationToken ct = default)
        {
            var until = ClinicTime.Today.AddDays(_options.FollowUpListAheadDays);
            var since = ClinicTime.Today.AddMonths(-3);
            var q = _db.FollowUps.AsNoTracking().Include(f => f.Patient).Include(f => f.Doctor).Include(f => f.FromReserve)
                .Include(f => f.Reserve).ThenInclude(r => r!.Status).AsQueryable();
            if (doctorId != null) q = q.Where(f => f.DoctorId == doctorId);
            q = tab switch
            {
                FollowUpTab.Upcoming => q.Where(f => f.Status == FollowUpStatus.Due && f.DueDate > until).OrderBy(f => f.DueDate),
                FollowUpTab.Booked => q.Where(f => f.Status == FollowUpStatus.Booked && f.DueDate >= since).OrderByDescending(f => f.DueDate),
                FollowUpTab.Closed => q.Where(f => f.Status == FollowUpStatus.Closed && f.DueDate >= since).OrderByDescending(f => f.DueDate),
                _ => q.Where(f => f.Status == FollowUpStatus.Due && f.DueDate <= until).OrderBy(f => f.DueDate)
            };
            return await q.Take(500).ToListAsync(ct);
        }

        public Task<List<Doctor>> DoctorsAsync(CancellationToken ct = default) =>
            _db.Doctors.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct);

        public Task<FollowUp?> FollowUpAsync(int id, CancellationToken ct = default) =>
            _db.FollowUps.AsNoTracking().Include(f => f.Patient).FirstOrDefaultAsync(f => f.Id == id, ct);
    }
}
