using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Payments and refunds at the cashier, and the period report.</summary>
    [Collection(DbCollection.Name)]
    public class KassaHttpTests
    {
        private readonly AppFixture _app;
        public KassaHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Pay_cancel_and_refund()
        {
            var cashier = await WebSession.LoginAsync(_app, AppRoles.Cashier);
            var reception = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var id = Db(db => TestData.Reserve(db, TestData.Patient(db), StatusNames.Confirmed, TestData.Exam(db)).Id); // 20 ₼
            var page = $"/Kassa/Pay/{id}";

            var tooMuch = await cashier.PostAsync(page, "/Kassa/Pay", ("ReserveId", id.ToString()), ("Amount", "25"), ("Method", "Cash"));
            Assert.Contains("qalıq borcdan", await WebSession.TextAsync(tooMuch));

            var paid = await cashier.PostAsync(page, "/Kassa/Pay", ("ReserveId", id.ToString()), ("Amount", "20"), ("Method", "Card"));
            Assert.Contains("ödəniş qeydə alındı", await cashier.FollowAsync(paid));
            var again = await cashier.PostAsync(page, "/Kassa/Pay", ("ReserveId", id.ToString()), ("Amount", "1"), ("Method", "Cash"));
            Assert.Equal(System.Net.HttpStatusCode.OK, again.StatusCode); // form shown again, nothing stored
            Assert.Equal(20, Db(db => db.Payments.Where(p => p.ReserveId == id).Sum(p => p.Amount)));

            // Cancelled after payment: the money is owed back, at most what was paid.
            var cancel = await reception.PostAsync($"/Reservation/Details/{id}", "/Reservation/Cancel", ("Id", id.ToString()));
            Assert.Contains("qaytarılmalıdır", await reception.FollowAsync(cancel));
            var over = await cashier.PostAsync(page, "/Kassa/Refund", ("ReserveId", id.ToString()), ("Amount", "21"), ("Method", "Card"));
            Assert.Contains("Ən çox", await WebSession.TextAsync(over));
            var refund = await cashier.PostAsync(page, "/Kassa/Refund", ("ReserveId", id.ToString()), ("Amount", "20"), ("Method", "Card"));
            Assert.Contains("xəstəyə qaytarıldı", await cashier.FollowAsync(refund));
            Assert.Equal(0, Db(db => db.Payments.Where(p => p.ReserveId == id).Sum(p => p.Amount)));
        }

        [Fact]
        public async Task Report_shows_the_period_totals()
        {
            var manager = await WebSession.LoginAsync(_app, AppRoles.Manager);
            var r = await manager.Client.GetAsync($"/Report?from={ClinicTime.Today.AddDays(-7):yyyy-MM-dd}&to={ClinicTime.Today:yyyy-MM-dd}");
            r.EnsureSuccessStatusCode();
            Assert.Contains("Nağd", await WebSession.TextAsync(r));
        }
    }
}
