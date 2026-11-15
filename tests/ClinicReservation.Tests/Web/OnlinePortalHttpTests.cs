using System.Net;
using System.Text.RegularExpressions;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Notifications;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec D1–D3: online booking with an SMS code and its limits, moving a visit with the link, rating it.</summary>
    [Collection(DbCollection.Name)]
    public class OnlinePortalHttpTests
    {
        private readonly AppFixture _app;
        public OnlinePortalHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");
        private static string NewPhone() => "050" + Random.Shared.Next(1_000_000, 9_999_999);
        private static string Normalized(string phone) => "994" + phone[1..];
        private static string NewPin() => Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

        private HttpClient Anonymous() => _app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        private static async Task<HttpResponseMessage> PostAsync(HttpClient c, string page, string action, params (string, string)[] fields)
        {
            var token = await WebSession.TokenAsync(c, page);
            var data = fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)).Append(new("__RequestVerificationToken", token));
            return await c.PostAsync(action, new FormUrlEncodedContent(data));
        }

        private (int DoctorId, int ExamId) DoctorAndExam() => Db(db => (TestData.Doctor(db).Id, TestData.Exam(db).Id));

        private static (string, string)[] Form(int examId, DateTime at, string pin, string phone, string? website = null) => new[]
        {
            ("ExamIds", examId.ToString()), ("Date", D(at.Date)), ("Time", at.ToString("HH:mm")), ("PinCode", pin), ("Phone", phone),
            ("Name", "Onlayn"), ("Surname", "Xəstə"), ("Birthday", "1990-02-03"), ("Website", website ?? "")
        };

        [Fact]
        public async Task A_new_patient_books_online_with_the_SMS_code()
        {
            var (doctorId, examId) = DoctorAndExam();
            var c = Anonymous();
            Assert.Contains("Onlayn yazılış", WebSession_Decode(await c.GetStringAsync("/online")));
            var page = $"/online/doctor/{doctorId}?examIds={examId}&date={D(ClinicTime.Today.AddDays(2))}";
            Assert.Contains("10:00", await c.GetStringAsync(page));

            // A bot fills the hidden field: refused.
            var bot = await PostAsync(c, page, $"/online/doctor/{doctorId}", Form(examId, ClinicTime.Today.AddDays(2).AddHours(10), NewPin(), NewPhone(), "http://spam"));
            Assert.Contains("Sorğu qəbul edilmədi", WebSession_Decode(await bot.Content.ReadAsStringAsync()));

            var pin = NewPin();
            var phone = NewPhone();
            var start = await PostAsync(c, page, $"/online/doctor/{doctorId}", Form(examId, ClinicTime.Today.AddDays(2).AddHours(10), pin, phone));
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            var verify = start.Headers.Location!.ToString();
            Assert.StartsWith("/online/verify/", verify);
            Assert.False(Db(db => db.Patients.Any(p => p.PinCode == pin)));          // nothing booked before the code
            Assert.Equal("•••• (təsdiq kodu)", Db(db => db.SmsMessages.Where(m => m.Phone == Normalized(phone)).Select(m => m.Text).Single()));
            var code = Regex.Match(_app.Sms.LastTo(Normalized(phone))!, @"\b\d{6}\b").Value;

            var wrong = await PostAsync(c, verify, verify, ("code", code == "000000" ? "111111" : "000000"));
            Assert.Contains("Kod səhvdir (4 cəhd qalıb)", WebSession_Decode(await wrong.Content.ReadAsStringAsync()));

            var ok = await PostAsync(c, verify, verify, ("code", code));
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            Assert.EndsWith("?new=1", ok.Headers.Location!.ToString());
            var booked = Db(db => db.Reserves.Include(r => r.Patient).AsNoTracking().Single(r => r.Patient.PinCode == pin));
            Assert.Equal((ReserveSource.Online, ClinicTime.Today.AddDays(2).AddHours(10), doctorId), (booked.Source, booked.AppointmentAt, booked.DoctorId!.Value));
            Assert.Contains("Qəbulunuz yazıldı", WebSession_Decode(await c.GetStringAsync(ok.Headers.Location!.ToString())));

            // The same code can't be used again.
            Assert.Contains("etibarsızdır", WebSession_Decode(await (await PostAsync(c, page, verify, ("code", code))).Content.ReadAsStringAsync()));
        }

        [Fact]
        public async Task Online_booking_is_limited_and_protected()
        {
            var (doctorId, examId) = DoctorAndExam();
            var c = Anonymous();
            var page = $"/online/doctor/{doctorId}";
            var tomorrow = ClinicTime.Today.AddDays(1).AddHours(12);
            async Task<string> Try((string, string)[] form) => WebSession_Decode(await (await PostAsync(c, page, page, form)).Content.ReadAsStringAsync());

            // Too soon / too far ahead.
            var soon = ClinicTime.Now.AddMinutes(30);
            if (soon.Date == ClinicTime.Today && soon.Hour >= 8 && soon.Hour < 20)
                Assert.Contains("ən azı 2 saat", await Try(Form(examId, new DateTime(soon.Year, soon.Month, soon.Day, soon.Hour, soon.Minute >= 30 ? 30 : 0, 0), NewPin(), NewPhone())));
            Assert.Contains("Ən çox 30 gün", await Try(Form(examId, ClinicTime.Today.AddDays(40).AddHours(10), NewPin(), NewPhone())));

            // A known FİN with another phone.
            var known = Db(db => TestData.Patient(db));
            Assert.Contains("telefon nömrəsi fərqlidir", await Try(Form(examId, tomorrow, known.PinCode, NewPhone())));

            // Three online visits ahead already: no fourth.
            Db(db =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var r = TestData.Reserve(db, db.Patients.Find(known.Id)!, StatusNames.Waiting, db.ExaminationTypes.Find(examId)!);
                    r.Source = ReserveSource.Online;
                    r.AppointmentAt = ClinicTime.Today.AddDays(3 + i).AddHours(9);
                }
                return db.SaveChanges();
            });
            Assert.Contains("ən çox 3 aktiv qəbul", await Try(Form(examId, tomorrow, known.PinCode, known.Phone)));

            // Codes: one a minute, three an hour; five wrong tries end the code; it expires.
            var phone = NewPhone();
            var first = await PostAsync(c, page, page, Form(examId, tomorrow, NewPin(), phone));
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
            Assert.Contains("saniyə gözləyin", await Try(Form(examId, tomorrow, NewPin(), phone)));
            Db(db => { foreach (var o in db.OtpCodes.Where(o => o.Phone == Normalized(phone))) o.CreatedAt = o.CreatedAt.AddMinutes(-2); return db.SaveChanges(); });
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(c, page, page, Form(examId, tomorrow, NewPin(), phone))).StatusCode);
            Db(db => { foreach (var o in db.OtpCodes.Where(o => o.Phone == Normalized(phone))) o.CreatedAt = o.CreatedAt.AddMinutes(-2); return db.SaveChanges(); });
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(c, page, page, Form(examId, tomorrow, NewPin(), phone))).StatusCode);
            Db(db => { foreach (var o in db.OtpCodes.Where(o => o.Phone == Normalized(phone))) o.CreatedAt = o.CreatedAt.AddMinutes(-2); return db.SaveChanges(); });
            Assert.Contains("çox kod göndərilib", await Try(Form(examId, tomorrow, NewPin(), phone)));

            var verify = first.Headers.Location!.ToString();
            for (var i = 0; i < 5; i++) await PostAsync(c, verify, verify, ("code", "999999"));
            Assert.Contains("vaxtı bitib və ya artıq istifadə olunub", WebSession_Decode(await c.GetStringAsync(verify)));
        }

        [Fact]
        public async Task The_patient_moves_the_visit_with_the_link_at_most_twice_and_rates_it_after()
        {
            var (doctorId, examId) = DoctorAndExam();
            var (id, token, patientName) = Db(db =>
            {
                var p = TestData.Patient(db);
                var r = TestData.Reserve(db, p, StatusNames.Confirmed, db.ExaminationTypes.Find(examId)!);
                r.DoctorId = doctorId;
                r.AppointmentAt = ClinicTime.Today.AddDays(3).AddHours(10);
                r.PatientToken = Guid.NewGuid().ToString("N");
                db.SaveChanges();
                return (r.Id, r.PatientToken, p.Name);
            });
            var c = Anonymous();
            var home = await c.GetStringAsync($"/r/{token}");
            Assert.Contains("Vaxtı dəyiş", WebSession_Decode(home));

            var day = ClinicTime.Today.AddDays(4);
            Assert.Contains("11:00", await c.GetStringAsync($"/r/{token}/move?date={D(day)}"));
            var moved = await PostAsync(c, $"/r/{token}/move?date={D(day)}", $"/r/{token}/move", ("date", D(day)), ("time", "11:00"));
            Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);
            var row = Db(db => db.Reserves.Include(r => r.Status).AsNoTracking().Single(r => r.Id == id));
            Assert.Equal((day.AddHours(11), 1, StatusNames.Waiting), (row.AppointmentAt, row.RescheduleCount, row.Status!.Name)); // confirm again
            Assert.True(Db(db => db.AuditLogs.Any(a => a.EntityId == id.ToString() && a.Action == "Xəstə SMS linki ilə vaxtı dəyişdi")));

            var second = await PostAsync(c, $"/r/{token}/move?date={D(day)}", $"/r/{token}/move", ("date", D(day)), ("time", "12:00"));
            var third = await c.GetStringAsync($"/r/{token}/move?date={D(day)}");
            Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
            Assert.Contains("ən çox 2 dəfə", WebSession_Decode(third));

            // Completed → asked to rate (SMS), and the patient rates; the score can be changed.
            var admin = await WebSession.LoginAsync(_app, AppRoles.Admin);
            foreach (var to in new[] { StatusNames.Arrived, StatusNames.InProgress, StatusNames.Completed })
                await admin.PostAsync($"/Reservation/Details/{id}", "/Reservation/Transition", ("id", id.ToString()), ("to", to), ("returnTo", "details"));
            Assert.True(Db(db => db.SmsMessages.Any(m => m.ReserveId == id && m.Kind == SmsService.KindRating && m.Text.Contains($"/r/{token}/rate"))));
            await PostAsync(c, $"/r/{token}", $"/r/{token}/rate", ("score", "2"), ("comment", "Çox gözlədim"));
            await PostAsync(c, $"/r/{token}", $"/r/{token}/rate", ("score", "4"), ("comment", "Həkim yaxşı idi"));
            var rating = Db(db => db.VisitRatings.AsNoTracking().Single(r => r.ReserveId == id));
            Assert.Equal((4, "Həkim yaxşı idi", doctorId), (rating.Score, rating.Comment, rating.DoctorId!.Value));

            var manager = await WebSession.LoginAsync(_app, AppRoles.Manager);
            var report = WebSession_Decode(await manager.Client.GetStringAsync($"/Rating?doctorId={doctorId}"));
            Assert.Contains("Həkim yaxşı idi", report);

            // Too late to rate.
            Db(db => { db.Reserves.Single(r => r.Id == id).CompletedAt = ClinicTime.Now.AddDays(-20); return db.SaveChanges(); });
            await PostAsync(c, $"/r/{token}", $"/r/{token}/rate", ("score", "1"));
            Assert.Equal(4, Db(db => db.VisitRatings.Single(r => r.ReserveId == id).Score));
        }

        private static string WebSession_Decode(string html) => WebUtility.HtmlDecode(html);
    }
}
