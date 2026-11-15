using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>«Gəlmədi»: marking overdue visits automatically, and the per-patient counter behind the booking warning.</summary>
    public class NoShows
    {
        public const string Actor = "Sistem";
        /// <summary>At most this many per round, so a first run on old data doesn't hold one long transaction.</summary>
        public const int Batch = 500;

        private readonly IAppDbContext _db;
        private readonly AuditService _audit;
        private readonly SchedulingOptions _options;

        public NoShows(IAppDbContext db, AuditService audit, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _audit = audit;
            _options = options.Value;
        }

        public SchedulingOptions Options => _options;

        /// <summary>Visits nobody checked in, <see cref="SchedulingOptions.AutoNoShowAfterMinutes"/> after they ended → «Gəlmədi».</summary>
        public async Task<int> MarkOverdueAsync(DateTime now, CancellationToken ct = default)
        {
            if (_options.AutoNoShowAfterMinutes <= 0) return 0;
            var grace = _options.AutoNoShowAfterMinutes;
            // AppointmentAt + duration + grace <= now; the duration is per row, so filter loosely in SQL first.
            var latest = now.AddMinutes(-grace);
            var due = await _db.Reserves.Include(r => r.Patient).Include(r => r.Status)
                .Where(r => (r.Status!.Name == StatusNames.Waiting || r.Status.Name == StatusNames.Confirmed) && r.AppointmentAt <= latest)
                .OrderBy(r => r.AppointmentAt).Take(Batch * 2).ToListAsync(ct);
            due = due.Where(r => r.AppointmentAt.AddMinutes(r.DurationMinutes + grace) <= now).Take(Batch).ToList();
            if (due.Count == 0) return 0;

            var noShow = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.NoShow, ct);
            foreach (var r in due)
            {
                r.Status = noShow;
                r.NoShowAt = now;
                r.NoShowAuto = true;
            }
            await _db.SaveChangesAsync(ct);
            foreach (var r in due)
                _audit.Log("Avtomatik «Gəlmədi»", AuditEntities.Reservation, r.Id,
                    $"{r.Patient.FullName} — {Fmt.DateTime(r.AppointmentAt)}, {grace} dəq ərzində qeydiyyat olmadı", userName: Actor);
            return due.Count;
        }

        private DateTime Since => ClinicTime.Today.AddMonths(-Math.Max(1, _options.NoShowWindowMonths));

        /// <summary>Missed visits of the patient in the window.</summary>
        public Task<int> CountAsync(int patientId, CancellationToken ct = default)
        {
            var since = Since;
            return _db.Reserves.CountAsync(r => r.PatientId == patientId && r.Status!.Name == StatusNames.NoShow && r.AppointmentAt >= since, ct);
        }

        /// <summary>Patients among <paramref name="patientIds"/> who reached the warning count, with their count.</summary>
        public async Task<Dictionary<int, int>> RiskyAsync(IEnumerable<int> patientIds, CancellationToken ct = default)
        {
            if (_options.NoShowWarnAfter <= 0) return new();
            var ids = patientIds.Distinct().ToList();
            if (ids.Count == 0) return new();
            var since = Since;
            var min = _options.NoShowWarnAfter;
            return await _db.Reserves
                .Where(r => ids.Contains(r.PatientId) && r.Status!.Name == StatusNames.NoShow && r.AppointmentAt >= since)
                .GroupBy(r => r.PatientId)
                .Select(g => new { g.Key, Count = g.Count() })
                .Where(x => x.Count >= min)
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        }

        /// <summary>The booking warning for this count, or null below the threshold.</summary>
        public string? Warning(int count) =>
            _options.NoShowWarnAfter > 0 && count >= _options.NoShowWarnAfter
                ? $"Diqqət: xəstə son {_options.NoShowWindowMonths} ayda {count} dəfə qəbula gəlməyib. Qəbulu bir gün əvvəl telefonla təsdiqləyin."
                : null;
    }

    /// <summary>Every few minutes: overdue visits become «Gəlmədi» (one instance at a time).</summary>
    public class NoShowWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<NoShowWorker> _logger;
        private readonly SchedulingOptions _options;

        public NoShowWorker(IServiceProvider services, IOptions<SchedulingOptions> options, ILogger<NoShowWorker> logger)
        {
            _services = services;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.AutoNoShowAfterMinutes <= 0) return;
            // Let migrations/seed finish first.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var marked = 0;
                    await scope.ServiceProvider.GetRequiredService<IDistributedLock>().TryRunAsync(LockKeys.NoShows,
                        async () => marked = await scope.ServiceProvider.GetRequiredService<NoShows>().MarkOverdueAsync(ClinicTime.Now, stoppingToken),
                        stoppingToken);
                    if (marked > 0) _logger.LogInformation("{Count} rezervasiya avtomatik «Gəlmədi» edildi", marked);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Avtomatik «Gəlmədi» işi xəta verdi");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
