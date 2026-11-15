using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>
    /// The link in the patient's SMS: confirm or cancel without logging in, and see lab results
    /// after typing the birth date. The random token in the URL is the only key.
    /// </summary>
    public class PatientLinks
    {
        public const string Actor = "Xəstə (SMS linki)";
        private const string LabFailAction = "Onlayn nəticə: səhv doğum tarixi";
        private const int MaxFailuresPerHour = 5;

        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public PatientLinks(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        /// <summary>Only before the visit and while the booking is still open.</summary>
        public static bool CanAnswer(Reserve r) =>
            r.AppointmentAt > ClinicTime.Now && r.Status?.Name is StatusNames.Waiting or StatusNames.Confirmed;

        public Task<Reserve?> ByTokenAsync(string? token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return Task.FromResult<Reserve?>(null);
            return _db.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.Room)
                .FirstOrDefaultAsync(r => r.PatientToken == token, ct);
        }

        public async Task ConfirmAsync(Reserve res, CancellationToken ct = default)
        {
            if (!CanAnswer(res) || res.Status?.Name != StatusNames.Waiting) return;
            res.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Confirmed, ct);
            res.ConfirmDate = ClinicTime.Now;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Xəstə SMS linki ilə təsdiq etdi", AuditEntities.Reservation, res.Id, res.Patient.FullName, userName: Actor);
        }

        public async Task CancelAsync(Reserve res, CancellationToken ct = default)
        {
            if (!CanAnswer(res)) return;
            res.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Cancelled, ct);
            res.CancelDate = ClinicTime.Now;
            res.CancelReason = "Xəstə SMS linki ilə ləğv etdi";
            res.CancelledBy = CancelledBy.Patient;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Xəstə SMS linki ilə ləğv etdi", AuditEntities.Reservation, res.Id, res.Patient.FullName, userName: Actor);
        }

        /// <summary>Validated results after the birth-date check; at most 5 wrong tries an hour per link.</summary>
        public async Task<(List<LabOrder>? Orders, string? Error)> LabResultsAsync(Reserve res, DateTime? birthday, CancellationToken ct = default)
        {
            var since = ClinicTime.Now.AddHours(-1);
            var failures = await _db.AuditLogs.CountAsync(a => a.Action == LabFailAction && a.EntityId == res.Id.ToString() && a.At >= since, ct);
            if (failures >= MaxFailuresPerHour)
                return (null, "Çox sayda səhv cəhd. Bir saat sonra yenidən yoxlayın və ya klinikaya zəng edin.");
            if (birthday?.Date != res.Patient.Birthday.Date)
            {
                _audit.Log(LabFailAction, AuditEntities.Lab, res.Id, res.Patient.FullName, userName: Actor);
                return (null, "Doğum tarixi uyğun gəlmir");
            }

            var orders = await _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Reserve).ThenInclude(r => r.Doctor)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .Where(o => o.ReserveId == res.Id && o.Status == LabOrderStatus.Validated)
                .OrderBy(o => o.CollectedAt)
                .AsSplitQuery()
                .ToListAsync(ct);
            _audit.Log("Xəstə nəticəyə onlayn baxdı", AuditEntities.Lab, res.Id, res.Patient.FullName, userName: Actor);
            return (orders, null);
        }
    }
}
