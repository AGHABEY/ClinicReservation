using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>The doctor's desk, the hall screen and the patient's SMS link.</summary>
    [Collection(DbCollection.Name)]
    public class QueueAndPatientLinkHttpTests
    {
        private readonly AppFixture _app;
        public QueueAndPatientLinkHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Call_next_shows_the_ticket_on_the_screen_without_the_name()
        {
            var reception = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var doctor = await WebSession.LoginAsync(_app, AppRoles.Doctor);
            var (doctorId, reserveId, surname) = Db(db =>
            {
                var d = TestData.Doctor(db);
                var p = TestData.Patient(db);
                var r = TestData.Reserve(db, p, StatusNames.Confirmed, TestData.Exam(db));
                r.DoctorId = d.Id;
                r.AppointmentAt = ClinicTime.Today.AddHours(9);
                db.SaveChanges();
                return (d.Id, r.Id, p.Surname);
            });

            var empty = await doctor.PostAsync($"/Workspace?doctorId={doctorId}", "/Workspace/CallNext", ("doctorId", doctorId.ToString()));
            Assert.Contains("Növbədə gözləyən xəstə yoxdur", await doctor.FollowAsync(empty));

            await reception.PostAsync($"/Reservation/Details/{reserveId}", "/Reservation/Transition", ("id", reserveId.ToString()), ("to", StatusNames.Arrived), ("returnTo", "details"));
            var ticket = Db(db => db.Reserves.Single(r => r.Id == reserveId).QueueTicket);
            Assert.False(string.IsNullOrEmpty(ticket));

            var call = await doctor.PostAsync($"/Workspace?doctorId={doctorId}", "/Workspace/CallNext", ("doctorId", doctorId.ToString()));
            Assert.Contains("çağırıldı", await doctor.FollowAsync(call));

            var screen = await reception.Client.GetStringAsync("/Queue/Data");
            Assert.Contains(ticket!, screen);
            Assert.DoesNotContain(surname, screen);

            var printed = await reception.Client.GetAsync($"/Queue/Ticket/{reserveId}");
            Assert.Equal(HttpStatusCode.OK, printed.StatusCode);
        }

        [Fact]
        public async Task Patient_link_confirms_and_protects_lab_results()
        {
            var (token, reserveId, birthday) = Db(db =>
            {
                var p = TestData.Patient(db);
                var r = TestData.Reserve(db, p, StatusNames.Waiting, TestData.Exam(db));
                r.AppointmentAt = ClinicTime.Today.AddDays(2).AddHours(10);
                r.PatientToken = Guid.NewGuid().ToString("N");
                db.SaveChanges();
                return (r.PatientToken, r.Id, p.Birthday);
            });
            var anon = _app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/r/" + new string('x', 32))).StatusCode);

            var t = await WebSession.TokenAsync(anon, $"/r/{token}");
            await anon.PostAsync($"/r/{token}/confirm", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = t }));
            Assert.Equal(StatusNames.Confirmed, Db(db => db.Reserves.Include(r => r.Status).Single(r => r.Id == reserveId).Status!.Name));

            // Five wrong birth dates lock the results for an hour, even for the right date afterwards.
            for (var i = 0; i < 5; i++)
            {
                var lt = await WebSession.TokenAsync(anon, $"/r/{token}/lab");
                var wrong = await anon.PostAsync($"/r/{token}/lab", new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["birthday"] = "2000-01-01", ["__RequestVerificationToken"] = lt }));
                Assert.Contains("Doğum tarixi uyğun gəlmir", WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()));
            }
            var lt2 = await WebSession.TokenAsync(anon, $"/r/{token}/lab");
            var locked = await anon.PostAsync($"/r/{token}/lab", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["birthday"] = birthday.ToString("yyyy-MM-dd"), ["__RequestVerificationToken"] = lt2 }));
            Assert.Contains("Çox sayda səhv cəhd", WebUtility.HtmlDecode(await locked.Content.ReadAsStringAsync()));
        }
    }
}
