using System.Text.Json;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Events
{
    /// <summary>
    /// Events between modules. <see cref="Add"/> stores the event with the caller's next SaveChanges (one
    /// transaction with the change itself); <see cref="DispatchAddedAsync"/> then handles it right away so
    /// the user sees the outcome. Anything not handled (an error, a crash in between) is picked up again
    /// by <see cref="OutboxWorker"/>. Handlers must tolerate seeing an event twice.
    /// </summary>
    public class Outbox
    {
        public const int MaxAttempts = 10;

        private static readonly Dictionary<string, Type> Types = typeof(IIntegrationEvent).Assembly.GetTypes()
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToDictionary(t => t.Name);

        private readonly IAppDbContext _db;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<Outbox> _logger;
        private readonly List<OutboxMessage> _added = new();

        public Outbox(IAppDbContext db, IServiceScopeFactory scopes, ILogger<Outbox> logger)
        {
            _db = db;
            _scopes = scopes;
            _logger = logger;
        }

        /// <summary>Queues the event; it is stored by the caller's next SaveChanges.</summary>
        public void Add(IIntegrationEvent e)
        {
            var m = new OutboxMessage { Type = e.GetType().Name, Payload = JsonSerializer.Serialize(e, e.GetType()), OccurredAt = ClinicTime.Now };
            _db.OutboxMessages.Add(m);
            _added.Add(m);
        }

        /// <summary>Handles the events this unit of work added (after its SaveChanges). Returns warnings for the user.</summary>
        public async Task<List<string>> DispatchAddedAsync(CancellationToken ct = default)
        {
            var ids = _added.Where(m => m.Id != 0).Select(m => m.Id).ToList();
            _added.Clear();
            return ids.Count == 0 ? new() : await DispatchAsync(ids, null, ct);
        }

        /// <summary>
        /// Handles unprocessed messages (only <paramref name="ids"/> when given; otherwise those older than
        /// <paramref name="olderThan"/>), each in a fresh scope so a failing handler can't disturb the caller.
        /// </summary>
        public async Task<List<string>> DispatchAsync(IReadOnlyCollection<long>? ids, DateTime? olderThan, CancellationToken ct = default)
        {
            var warnings = new List<string>();
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var q = db.OutboxMessages.Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts);
            q = ids != null ? q.Where(m => ids.Contains(m.Id)) : q.Where(m => m.OccurredAt < olderThan);
            var messages = await q.OrderBy(m => m.Id).Take(100).ToListAsync(ct);

            foreach (var m in messages)
            {
                try
                {
                    if (!Types.TryGetValue(m.Type, out var type)) throw new InvalidOperationException($"Naməlum hadisə: {m.Type}");
                    var e = (IIntegrationEvent)JsonSerializer.Deserialize(m.Payload, type)!;
                    using var handlerScope = _scopes.CreateScope();
                    foreach (var h in handlerScope.ServiceProvider.GetServices<IEventHandler>().Where(h => h.EventType == type))
                        if (await h.HandleAsync(e, ct) is { } w) warnings.Add(w);
                    m.ProcessedAt = ClinicTime.Now;
                    m.LastError = null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m.Attempts++;
                    m.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    _logger.LogError(ex, "Hadisə emal olunmadı: {Type} #{Id} (cəhd {Attempts})", m.Type, m.Id, m.Attempts);
                    warnings.Add(m.Type switch
                    {
                        nameof(VisitCompleted) or nameof(LabSampleCollected) => "Materiallar anbardan hələ silinmədi — sistem bir neçə dəqiqə ərzində yenidən cəhd edəcək.",
                        nameof(DoseGiven) => "Dərman anbardan hələ silinmədi — sistem bir neçə dəqiqə ərzində yenidən cəhd edəcək.",
                        _ => "Əlaqəli əməliyyat hələ tamamlanmadı — sistem yenidən cəhd edəcək."
                    });
                }
                await db.SaveChangesAsync(ct);
            }
            return warnings;
        }
    }

    /// <summary>Retries events that weren't handled right away (one server at a time).</summary>
    public class OutboxWorker : BackgroundService
    {
        public const long LockKey = 7_301_003;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
        /// <summary>The request that raised an event handles it first; the worker only takes older ones.</summary>
        private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<OutboxWorker> _logger;

        public OutboxWorker(IServiceScopeFactory scopes, ILogger<OutboxWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Outbox işi xəta verdi");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }

        public async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<Outbox>();
            await scope.ServiceProvider.GetRequiredService<IDistributedLock>()
                .TryRunAsync(LockKey, () => outbox.DispatchAsync(null, ClinicTime.Now - Grace, ct), ct);
        }
    }
}
