using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    [Collection(DbCollection.Name)]
    public class SmsAndAuditHttpTests
    {
        private readonly AppFixture _app;
        public SmsAndAuditHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Manual_reminder_only_for_future_active_bookings_and_is_audited()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var (future, past) = Db(db =>
            {
                var f = TestData.Reserve(db, TestData.Patient(db), StatusNames.Waiting, TestData.Exam(db));
                f.AppointmentAt = ClinicTime.Today.AddDays(1).AddHours(10);
                var p = TestData.Reserve(db, TestData.Patient(db), StatusNames.Waiting, TestData.Exam(db));
                p.AppointmentAt = ClinicTime.Today.AddDays(-1).AddHours(10);
                db.SaveChanges();
                return (f.Id, p.Id);
            });

            var refused = await s.PostAsync($"/Reservation/Details/{past}", "/Sms/Send", ("reserveId", past.ToString()));
            Assert.Contains("yalnız gələcək, aktiv rezervasiyaya", await s.FollowAsync(refused));

            var sent = await s.PostAsync($"/Reservation/Details/{future}", "/Sms/Send", ("reserveId", future.ToString()));
            Assert.Contains("test rejimində qeydə alındı", await s.FollowAsync(sent));
            Assert.True(Db(db => db.SmsMessages.Any(m => m.ReserveId == future && m.Status == SmsStatus.Simulated)));

            var admin = await WebSession.LoginAsync(_app, AppRoles.Admin);
            var log = await admin.Client.GetStringAsync("/Audit?entity=SMS&q=t_resepsiya");
            Assert.Contains("SMS göndərildi", System.Net.WebUtility.HtmlDecode(log));
        }
    }
}
