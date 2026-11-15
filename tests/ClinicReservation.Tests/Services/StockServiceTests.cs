using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Services
{
    [Collection(DbCollection.Name)]
    public class StockServiceTests
    {
        private readonly AppFixture _app;
        public StockServiceTests(AppFixture app) => _app = app;

        private static readonly DateTime Today = ClinicTime.Today;

        private (IServiceScope Scope, AppDbContext Db, StockService Stock) Open()
        {
            var scope = _app.Scope();
            return (scope, scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<StockService>());
        }

        private static decimal Qty(AppDbContext db, int itemId, int warehouseId) =>
            db.StockBatches.AsNoTracking().Where(b => b.StockItemId == itemId && b.WarehouseId == warehouseId).Sum(b => b.Quantity);

        /// <summary>Every lot's quantity must equal the sum of its movements.</summary>
        private static void AssertLedgerBalanced(AppDbContext db, int itemId)
        {
            foreach (var lot in db.StockBatches.AsNoTracking().Where(b => b.StockItemId == itemId).ToList())
            {
                var moved = db.StockMovements.AsNoTracking().Where(m => m.StockBatchId == lot.Id).Sum(m => m.Quantity);
                Assert.Equal(moved, lot.Quantity);
                Assert.True(lot.Quantity >= 0);
            }
        }

        [Fact]
        public void Receipt_creates_lots_and_a_numbered_document()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var wh = TestData.Warehouse(db);
            var item = TestData.Item(db, StockItemKind.Medicine, "ampul");

            var o = stock.Receive(wh.Id, null, "QF-1", Today, null, new[]
            {
                new StockService.ReceiptLine(item.Id, 10, 0.8m, "A1", Today.AddMonths(6)),
                new StockService.ReceiptLine(item.Id, 5, 0.9m, "A2", Today.AddMonths(2)),
            }, "test");

            Assert.True(o.Ok, string.Join("; ", o.Errors));
            Assert.Matches(@"^MD-\d{5}$", o.Document!.Number);
            Assert.Equal(15, Qty(db, item.Id, wh.Id));
            AssertLedgerBalanced(db, item.Id);
        }

        [Fact]
        public void Receipt_rejects_medicine_without_expiry_and_expired_goods()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var wh = TestData.Warehouse(db);
            var med = TestData.Item(db, StockItemKind.Medicine);
            var glove = TestData.Item(db);

            var o = stock.Receive(wh.Id, null, null, Today, null, new[]
            {
                new StockService.ReceiptLine(med.Id, 10, 1, null, null),
                new StockService.ReceiptLine(glove.Id, 10, 1, null, Today.AddDays(-1)),
                new StockService.ReceiptLine(glove.Id, 0, 1, null, null),
            }, "test");

            Assert.False(o.Ok);
            Assert.Equal(3, o.Errors.Count);
            Assert.Equal(0, Qty(db, med.Id, wh.Id));
        }

        [Fact]
        public void Write_off_takes_the_lot_that_expires_first_and_skips_expired_lots()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var wh = TestData.Warehouse(db);
            var item = TestData.Item(db, StockItemKind.Medicine);
            stock.Receive(wh.Id, null, null, Today, null, new[]
            {
                new StockService.ReceiptLine(item.Id, 10, 1, "LATE", Today.AddMonths(12)),
                new StockService.ReceiptLine(item.Id, 4, 1, "SOON", Today.AddMonths(1)),
                new StockService.ReceiptLine(item.Id, 7, 1, "OLD", Today.AddMonths(3)),
            }, "test");
            // Make "OLD" expired after receiving it.
            db.StockBatches.Where(b => b.BatchNo == "OLD" && b.WarehouseId == wh.Id).ExecuteUpdate(s => s.SetProperty(b => b.ExpiryDate, Today.AddDays(-1)));
            db.ChangeTracker.Clear();

            var o = stock.WriteOff(wh.Id, "Prosedurda istifadə edildi", null, null, null, new[] { new StockService.OutLine(item.Id, 6) }, "test");

            Assert.True(o.Ok, string.Join("; ", o.Errors));
            var lots = db.StockBatches.AsNoTracking().Where(b => b.WarehouseId == wh.Id).ToDictionary(b => b.BatchNo!, b => b.Quantity);
            Assert.Equal(0, lots["SOON"]);
            Assert.Equal(8, lots["LATE"]);
            Assert.Equal(7, lots["OLD"]); // expired lot untouched
            AssertLedgerBalanced(db, item.Id);
        }

        [Fact]
        public void Expired_stock_only_leaves_as_expired_write_off()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var wh = TestData.Warehouse(db);
            var item = TestData.Item(db, StockItemKind.Medicine);
            stock.Receive(wh.Id, null, null, Today, null, new[] { new StockService.ReceiptLine(item.Id, 5, 1, "X", Today.AddDays(10)) }, "test");
            db.StockBatches.Where(b => b.WarehouseId == wh.Id).ExecuteUpdate(s => s.SetProperty(b => b.ExpiryDate, Today.AddDays(-2)));
            db.ChangeTracker.Clear(); // the update bypassed the context; forget the stale lot (its xmin changed)

            var toPatient = stock.WriteOff(wh.Id, "Xəstəyə verildi", null, null, null, new[] { new StockService.OutLine(item.Id, 1) }, "test");
            Assert.False(toPatient.Ok);

            var expired = stock.WriteOff(wh.Id, "Vaxtı keçib", null, null, null, new[] { new StockService.OutLine(item.Id, 5) }, "test");
            Assert.True(expired.Ok, string.Join("; ", expired.Errors));
            Assert.Equal(0, Qty(db, item.Id, wh.Id));
        }

        [Fact]
        public void Transfer_moves_lots_with_their_expiry_and_refuses_more_than_available()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var from = TestData.Warehouse(db);
            var to = TestData.Warehouse(db);
            var item = TestData.Item(db);
            stock.Receive(from.Id, null, null, Today, null, new[] { new StockService.ReceiptLine(item.Id, 10, 2, "B1", Today.AddMonths(4)) }, "test");

            Assert.False(stock.Transfer(from.Id, to.Id, null, new[] { new StockService.OutLine(item.Id, 11) }, "test").Ok);
            Assert.True(stock.Transfer(from.Id, to.Id, null, new[] { new StockService.OutLine(item.Id, 3) }, "test").Ok);
            Assert.True(stock.Transfer(from.Id, to.Id, null, new[] { new StockService.OutLine(item.Id, 2) }, "test").Ok);

            var dest = db.StockBatches.AsNoTracking().Where(b => b.WarehouseId == to.Id).ToList();
            var lot = Assert.Single(dest); // the second transfer joined the same lot
            Assert.Equal(5, lot.Quantity);
            Assert.Equal("B1", lot.BatchNo);
            Assert.Equal(Today.AddMonths(4), lot.ExpiryDate);
            Assert.Equal(5, Qty(db, item.Id, from.Id));
            AssertLedgerBalanced(db, item.Id);
        }

        [Fact]
        public void Receipt_cannot_be_cancelled_once_used_but_write_off_can()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var wh = TestData.Warehouse(db);
            var item = TestData.Item(db);
            var receipt = stock.Receive(wh.Id, null, null, Today, null, new[] { new StockService.ReceiptLine(item.Id, 10, 1, null, null) }, "test").Document!;
            var writeOff = stock.WriteOff(wh.Id, "Daxili istifadə", null, null, null, new[] { new StockService.OutLine(item.Id, 4) }, "test").Document!;

            var refused = stock.Cancel(receipt.Id, "səhv", "test");
            Assert.False(refused.Ok);
            Assert.Contains("artıq istifadə olunub", refused.Errors.Single());

            var undone = stock.Cancel(writeOff.Id, "səhv", "test");
            Assert.True(undone.Ok, string.Join("; ", undone.Errors));
            Assert.Equal(10, Qty(db, item.Id, wh.Id));
            Assert.False(stock.Cancel(writeOff.Id, "yenə", "test").Ok); // only once

            Assert.True(stock.Cancel(receipt.Id, "indi olar", "test").Ok);
            Assert.Equal(0, Qty(db, item.Id, wh.Id));
            AssertLedgerBalanced(db, item.Id);
        }

        [Fact]
        public void Two_users_taking_the_same_lot_at_once_cannot_both_win()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var wh = TestData.Warehouse(db);
            var item = TestData.Item(db);
            stock.Receive(wh.Id, null, null, Today, null, new[] { new StockService.ReceiptLine(item.Id, 5, 1, null, null) }, "test");

            // Second user: a separate request/context that loaded the lot before the first one saved.
            using var other = _app.Scope();
            var otherStock = other.ServiceProvider.GetRequiredService<StockService>();
            var otherDb = other.ServiceProvider.GetRequiredService<AppDbContext>();
            otherDb.StockBatches.Where(b => b.WarehouseId == wh.Id).Load();

            Assert.True(stock.WriteOff(wh.Id, "Daxili istifadə", null, null, null, new[] { new StockService.OutLine(item.Id, 4) }, "u1").Ok);
            var late = otherStock.WriteOff(wh.Id, "Daxili istifadə", null, null, null, new[] { new StockService.OutLine(item.Id, 4) }, "u2");

            Assert.False(late.Ok);
            Assert.Equal(1, Qty(db, item.Id, wh.Id)); // never negative, never double-spent
            AssertLedgerBalanced(db, item.Id);
        }

        [Fact]
        public void Inventory_count_posts_only_the_difference()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var wh = TestData.Warehouse(db);
            var item = TestData.Item(db);
            stock.Receive(wh.Id, null, null, Today, null, new[] { new StockService.ReceiptLine(item.Id, 20, 1, null, null) }, "test");
            var lot = db.StockBatches.AsNoTracking().Single(b => b.WarehouseId == wh.Id);

            var o = stock.Count(wh.Id, null, new Dictionary<int, decimal> { [lot.Id] = 17 }, "test");

            Assert.True(o.Ok);
            var m = Assert.Single(o.Document!.Movements);
            Assert.Equal(-3, m.Quantity);
            Assert.Equal(17, m.Counted);
            Assert.Equal(17, Qty(db, item.Id, wh.Id));
        }

        [Fact]
        public void Visit_materials_come_from_the_department_store_and_shortages_are_noted_once()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var dept = TestData.Department(db);
            var wh = TestData.Warehouse(db, dept.Id);
            var syringe = TestData.Item(db);
            var gel = TestData.Item(db);
            stock.Receive(wh.Id, null, null, Today, null, new[]
            {
                new StockService.ReceiptLine(syringe.Id, 5, 0.1m, null, null),
                new StockService.ReceiptLine(gel.Id, 1, 3, null, null),
            }, "test");
            var exam = TestData.Exam(db, dept.Id, (syringe, 2), (gel, 3));
            var visit = TestData.Reserve(db, TestData.Patient(db), StatusNames.Completed, exam);

            var o = stock.ConsumeForReserve(visit.Id, false, "test");

            Assert.True(o.Ok, string.Join("; ", o.Errors));
            var doc = Assert.Single(o.Documents);
            Assert.Equal(wh.Id, doc.FromWarehouseId);
            Assert.Equal(3, Qty(db, syringe.Id, wh.Id));
            Assert.Equal(0, Qty(db, gel.Id, wh.Id));
            var shortage = Assert.Single(o.Shortages);
            Assert.Equal(2, shortage.Missing);
            Assert.Contains("çatışmadı", doc.Note);

            // Calling again (another click, a retry) takes nothing more.
            Assert.Empty(stock.ConsumeForReserve(visit.Id, false, "test").Documents);
            Assert.Equal(3, Qty(db, syringe.Id, wh.Id));
        }

        [Fact]
        public void Lab_collection_takes_only_lab_service_materials()
        {
            var (scope, db, stock) = Open();
            using var scopeLifetime = scope;
            var main = stock.MainWarehouse()!;
            var tube = TestData.Item(db);
            var glove = TestData.Item(db);
            stock.Receive(main.Id, null, null, Today, null, new[]
            {
                new StockService.ReceiptLine(tube.Id, 10, 0.1m, null, null),
                new StockService.ReceiptLine(glove.Id, 10, 0.1m, null, null),
            }, "test");
            var labExam = TestData.Exam(db, null, (tube, 1));
            labExam.LabTests.Add(new ExaminationTypeLabTest { LabTest = new LabTest { Code = TestData.Unique("L"), Name = "Test" } });
            db.SaveChanges();
            var visitExam = TestData.Exam(db, null, (glove, 1));
            var visit = TestData.Reserve(db, TestData.Patient(db), StatusNames.Arrived, labExam, visitExam);

            stock.ConsumeForReserve(visit.Id, true, "test");
            Assert.Equal(9, Qty(db, tube.Id, main.Id));
            Assert.Equal(10, Qty(db, glove.Id, main.Id));

            stock.ConsumeForReserve(visit.Id, false, "test"); // visit completed later
            Assert.Equal(9, Qty(db, tube.Id, main.Id));
            Assert.Equal(9, Qty(db, glove.Id, main.Id));
        }
    }
}
