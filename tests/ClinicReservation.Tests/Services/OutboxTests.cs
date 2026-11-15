using System.Text.Json;
using ClinicReservation.Data;
using ClinicReservation.Events;
using ClinicReservation.Models;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Services
{
    /// <summary>Events between modules: stored with the change, handled once, retried when they fail.</summary>
    [Collection(DbCollection.Name)]
    public class OutboxTests
    {
        private readonly AppFixture _app;
        public OutboxTests(AppFixture app) => _app = app;

        private (int ReserveId, int ItemId, int WarehouseId) VisitWithMaterial(AppDbContext db, StockService stock, decimal onHand)
        {
            var dept = TestData.Department(db);
            var wh = TestData.Warehouse(db, dept.Id);
            var item = TestData.Item(db);
            stock.Receive(wh.Id, null, null, ClinicTime.Today, null, new[] { new StockService.ReceiptLine(item.Id, onHand, 1, null, null) }, "test");
            var exam = TestData.Exam(db, dept.Id, (item, 2));
            var r = TestData.Reserve(db, TestData.Patient(db), StatusNames.Completed, exam);
            return (r.Id, item.Id, wh.Id);
        }

        [Fact]
        public async Task An_event_left_unhandled_is_picked_up_by_the_worker()
        {
            using var scope = _app.Scope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (reserveId, itemId, whId) = VisitWithMaterial(db, scope.ServiceProvider.GetRequiredService<StockService>(), 5);

            // As if the server stopped right after saving the visit: the event is stored but not handled.
            db.OutboxMessages.Add(new OutboxMessage
            {
                Type = nameof(VisitCompleted),
                Payload = JsonSerializer.Serialize(new VisitCompleted(reserveId, "test")),
                OccurredAt = ClinicTime.Now.AddMinutes(-5)
            });
            db.SaveChanges();

            await scope.ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OutboxWorker>().Single().RunOnceAsync(default);

            Assert.Equal(3, db.StockBatches.AsNoTracking().Where(b => b.WarehouseId == whId && b.StockItemId == itemId).Sum(b => b.Quantity));
            Assert.NotNull(db.OutboxMessages.AsNoTracking().Single(m => m.Payload.Contains($"\"ReserveId\":{reserveId},")).ProcessedAt);

            // Seeing the same event again takes nothing more.
            db.OutboxMessages.Add(new OutboxMessage { Type = nameof(VisitCompleted), Payload = JsonSerializer.Serialize(new VisitCompleted(reserveId, "test")), OccurredAt = ClinicTime.Now.AddMinutes(-5) });
            db.SaveChanges();
            await scope.ServiceProvider.GetRequiredService<Outbox>().DispatchAsync(null, ClinicTime.Now.AddMinutes(-1));
            Assert.Equal(3, db.StockBatches.AsNoTracking().Where(b => b.WarehouseId == whId && b.StockItemId == itemId).Sum(b => b.Quantity));
        }

        [Fact]
        public async Task A_failing_event_stays_for_a_retry_with_its_error()
        {
            using var scope = _app.Scope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bad = new OutboxMessage { Type = "NoSuchEvent", Payload = "{}", OccurredAt = ClinicTime.Now.AddMinutes(-5) };
            db.OutboxMessages.Add(bad);
            db.SaveChanges();

            await scope.ServiceProvider.GetRequiredService<Outbox>().DispatchAsync(new[] { bad.Id }, null);

            var after = db.OutboxMessages.AsNoTracking().Single(m => m.Id == bad.Id);
            Assert.Null(after.ProcessedAt);
            Assert.Equal(1, after.Attempts);
            Assert.Contains("NoSuchEvent", after.LastError);
            db.OutboxMessages.Remove(db.OutboxMessages.Single(m => m.Id == bad.Id)); // keep the shared database clean
            db.SaveChanges();
        }

        [Fact]
        public async Task Completing_a_visit_stores_the_event_with_it_and_handles_it_at_once()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            int reserveId, itemId, whId;
            using (var scope = _app.Scope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (reserveId, itemId, whId) = VisitWithMaterial(db, scope.ServiceProvider.GetRequiredService<StockService>(), 1);
                db.Reserves.Include(r => r.Status).Single(r => r.Id == reserveId).Status = db.ReserveStatuses.Single(x => x.Name == StatusNames.InProgress);
                db.SaveChanges();
            }

            var done = await s.PostAsync($"/Reservation/Details/{reserveId}", "/Reservation/Transition", ("id", reserveId.ToString()), ("to", StatusNames.Completed), ("returnTo", "details"));
            Assert.Contains("Anbarda çatışmayan material", await s.FollowAsync(done)); // 2 needed, 1 on hand

            using var check = _app.Scope();
            var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
            var msg = cdb.OutboxMessages.AsNoTracking().Single(m => m.Type == nameof(VisitCompleted) && m.Payload.Contains($"\"ReserveId\":{reserveId},"));
            Assert.NotNull(msg.ProcessedAt);
            Assert.Equal(0, cdb.StockBatches.AsNoTracking().Where(b => b.WarehouseId == whId && b.StockItemId == itemId).Sum(b => b.Quantity));
        }
    }
}
