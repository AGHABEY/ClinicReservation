using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>The pharmacy forms end to end: what the browser posts, what the user sees, what is stored.</summary>
    [Collection(DbCollection.Name)]
    public class PharmacyHttpTests
    {
        private readonly AppFixture _app;
        public PharmacyHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private (Warehouse From, Warehouse To, StockItem Drug, StockItem Glove) Setup() => Db(db =>
            (TestData.Warehouse(db), TestData.Warehouse(db), TestData.Item(db, StockItemKind.Medicine, "ampul"), TestData.Item(db)));

        [Fact]
        public async Task Receipt_then_transfer_write_off_and_cancel_through_the_forms()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Storekeeper);
            var (from, to, drug, glove) = Setup();
            var expiry = ClinicTime.Today.AddMonths(8).ToString("yyyy-MM-dd");

            // Receipt with one bad line: the form comes back with the error, nothing is stored.
            var bad = await s.PostAsync("/StockDoc/Receipt", "/StockDoc/Receipt",
                ("WarehouseId", from.Id.ToString()), ("Date", ClinicTime.Today.ToString("yyyy-MM-dd")),
                ("Lines[0].ItemId", drug.Id.ToString()), ("Lines[0].Quantity", "10"), ("Lines[0].UnitCost", "0.8"));
            Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
            Assert.Contains("son istifadə tarixini yazın", await WebSession.TextAsync(bad));

            var ok = await s.PostAsync("/StockDoc/Receipt", "/StockDoc/Receipt",
                ("WarehouseId", from.Id.ToString()), ("Date", ClinicTime.Today.ToString("yyyy-MM-dd")), ("InvoiceNo", "QF-9"),
                ("Lines[0].ItemId", drug.Id.ToString()), ("Lines[0].Quantity", "10"), ("Lines[0].UnitCost", "0.8"), ("Lines[0].ExpiryDate", expiry), ("Lines[0].BatchNo", "B1"),
                ("Lines[1].ItemId", glove.Id.ToString()), ("Lines[1].Quantity", "50"), ("Lines[1].UnitCost", "0.1"),
                ("Lines[2].ItemId", ""), ("Lines[2].Quantity", "")); // the empty row the form always sends
            var page = await s.FollowAsync(ok);
            Assert.Contains("mədaxil qeydə alındı", page);
            Assert.Contains("QF-9", page);

            var transfer = await s.PostAsync("/StockDoc/Transfer", "/StockDoc/Transfer",
                ("WarehouseId", from.Id.ToString()), ("ToWarehouseId", to.Id.ToString()),
                ("Lines[0].ItemId", glove.Id.ToString()), ("Lines[0].Quantity", "20"));
            Assert.Contains("köçürmə qeydə alındı", await s.FollowAsync(transfer));

            var tooMuch = await s.PostAsync("/StockDoc/WriteOff", "/StockDoc/WriteOff",
                ("WarehouseId", from.Id.ToString()), ("Reason", "Zədələnib / sınıb"),
                ("Lines[0].ItemId", glove.Id.ToString()), ("Lines[0].Quantity", "31"));
            Assert.Contains("istənilən 31", await WebSession.TextAsync(tooMuch));

            var writeOff = await s.PostAsync("/StockDoc/WriteOff", "/StockDoc/WriteOff",
                ("WarehouseId", from.Id.ToString()), ("Reason", "Zədələnib / sınıb"),
                ("Lines[0].ItemId", glove.Id.ToString()), ("Lines[0].Quantity", "5"));
            var woPage = await s.FollowAsync(writeOff);
            var docId = int.Parse(writeOff.Headers.Location!.ToString().Split('/').Last());

            var cancel = await s.PostAsync($"/StockDoc/Details/{docId}", "/StockDoc/Cancel", ("id", docId.ToString()), ("reason", "səhv"));
            Assert.Contains("ləğv edildi", await s.FollowAsync(cancel));

            Assert.Equal(30, Db(db => db.StockBatches.Where(b => b.WarehouseId == from.Id && b.StockItemId == glove.Id).Sum(b => b.Quantity)));
            Assert.Equal(20, Db(db => db.StockBatches.Where(b => b.WarehouseId == to.Id).Sum(b => b.Quantity)));
            Assert.True(Db(db => db.AuditLogs.Any(a => a.EntityType == "Anbar" && a.Action == "Anbar sənədi ləğv edildi" && a.UserName == "t_anbardar")));
        }

        [Fact]
        public async Task Inventory_count_form_records_the_difference()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Storekeeper);
            var (wh, _, _, glove) = Setup();
            await s.PostAsync("/StockDoc/Receipt", "/StockDoc/Receipt",
                ("WarehouseId", wh.Id.ToString()), ("Date", ClinicTime.Today.ToString("yyyy-MM-dd")),
                ("Lines[0].ItemId", glove.Id.ToString()), ("Lines[0].Quantity", "12"), ("Lines[0].UnitCost", "1"));
            var lot = Db(db => db.StockBatches.Single(b => b.WarehouseId == wh.Id).Id);

            var empty = await s.PostAsync($"/StockDoc/Count?warehouseId={wh.Id}", "/StockDoc/Count", ("WarehouseId", wh.Id.ToString()), ($"Counted[{lot}]", ""));
            Assert.Contains("Heç bir sətir doldurulmayıb", await WebSession.TextAsync(empty));

            var r = await s.PostAsync($"/StockDoc/Count?warehouseId={wh.Id}", "/StockDoc/Count", ("WarehouseId", wh.Id.ToString()), ($"Counted[{lot}]", "10"));
            Assert.Contains("1 sətirdə fərq qeydə alındı", await s.FollowAsync(r));
            Assert.Equal(10, Db(db => db.StockBatches.Single(b => b.Id == lot).Quantity));
        }

        [Fact]
        public async Task Settings_forms_validate_and_save()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Storekeeper);
            var code = TestData.Unique("C").ToUpperInvariant();

            var noName = await s.PostAsync("/StockItem/Create", "/StockItem/Save", ("Id", "0"), ("Name", ""), ("Kind", "Consumable"), ("Unit", "ədəd"), ("MinQuantity", "0"), ("IsActive", "true"));
            Assert.Equal(HttpStatusCode.OK, noName.StatusCode);
            Assert.Contains("Ad daxil edin", await WebSession.TextAsync(noName));

            var saved = await s.PostAsync("/StockItem/Create", "/StockItem/Save", ("Id", "0"), ("Name", "Test bint"), ("Code", code.ToLowerInvariant()), ("Kind", "Consumable"), ("Unit", "rulon"), ("MinQuantity", "5"), ("IsActive", "true"));
            Assert.Contains("«Test bint» əlavə edildi", await s.FollowAsync(saved));
            Assert.True(Db(db => db.StockItems.Any(i => i.Code == code && i.Unit == "rulon")));

            var dup = await s.PostAsync("/StockItem/Create", "/StockItem/Save", ("Id", "0"), ("Name", "Başqa"), ("Code", code), ("Kind", "Consumable"), ("Unit", "ədəd"), ("MinQuantity", "0"), ("IsActive", "true"));
            Assert.Contains("Bu kodla məhsul artıq var", await WebSession.TextAsync(dup));

            var badVoen = await s.PostAsync("/StockItem/Suppliers", "/StockItem/SaveSupplier", ("id", "0"), ("name", "X MMC"), ("voen", "12AB"));
            Assert.Contains("VÖEN 10 rəqəm", await s.FollowAsync(badVoen));
        }

        [Fact]
        public async Task Nurse_can_write_off_but_not_receive()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Nurse);
            Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/StockDoc/WriteOff")).StatusCode);
            var r = await s.Client.GetAsync("/StockDoc/Receipt");
            Assert.Contains("AccessDenied", r.Headers.Location!.ToString());
        }
    }
}
