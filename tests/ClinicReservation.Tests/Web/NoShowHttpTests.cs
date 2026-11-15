using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using ClinicReservation.Services;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec A4: automatic «Gəlmədi», late arrival, and the warning when booking a patient who often misses visits.</summary>
    [Collection(DbCollection.Name)]
    public class NoShowHttpTests
    {
        private readonly AppFixture _app;
        public NoShowHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private int Visit(int patientId, int doctorId, DateTime at, string status) => Db(db =>
        {
            var r = TestData.Reserve(db, db.Patients.Find(patientId)!, status, TestData.Exam(db));
            r.DoctorId = doctorId;
            r.AppointmentAt = at;
            r.DurationMinutes = 30;
            db.SaveChanges();
            return r.Id;
        });

        private string StatusOf(int id) => Db(db => db.Reserves.Include(r => r.Status).Single(r => r.Id == id).Status!.Name);

        [Fact]
        public async Task Overdue_visits_become_no_show_and_a_late_patient_can_still_be_checked_in_that_day()
        {
            var now = ClinicTime.Now;
            var (patientId, doctorId) = Db(db => (TestData.Patient(db).Id, TestData.Doctor(db).Id));
            var overdue = Visit(patientId, doctorId, now.AddMinutes(-100), StatusNames.Confirmed);   // ended 70 min ago
            var yesterday = Visit(patientId, doctorId, now.AddDays(-1), StatusNames.Waiting);
            var recent = Visit(patientId, doctorId, now.AddMinutes(-60), StatusNames.Waiting);       // ended 30 min ago
            var arrived = Visit(patientId, doctorId, now.AddMinutes(-100), StatusNames.Arrived);

            int marked;
            using (var scope = _app.Scope())
            {
                var sp = scope.ServiceProvider;
                var noShows = new NoShows(sp.GetRequiredService<IAppDbContext>(), sp.GetRequiredService<AuditService>(),
                    Microsoft.Extensions.Options.Options.Create(new SchedulingOptions { AutoNoShowAfterMinutes = 60 }));
                marked = await noShows.MarkOverdueAsync(now);
            }
            Assert.True(marked >= 2);
            Assert.Equal(StatusNames.NoShow, StatusOf(overdue));
            Assert.Equal(StatusNames.NoShow, StatusOf(yesterday));
            Assert.Equal(StatusNames.Waiting, StatusOf(recent));
            Assert.Equal(StatusNames.Arrived, StatusOf(arrived));
            var row = Db(db => db.Reserves.AsNoTracking().Single(r => r.Id == overdue));
            Assert.True(row.NoShowAuto);
            Assert.NotNull(row.NoShowAt);
            Assert.True(Db(db => db.AuditLogs.Any(a => a.EntityId == overdue.ToString() && a.Action == "Avtomatik «Gəlmədi»" && a.UserName == NoShows.Actor)));

            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            Assert.Contains("avtomatik qeyd etdi", await WebSession.TextAsync(await s.Client.GetAsync($"/Reservation/Details/{yesterday}")));

            // Yesterday's no-show stays; today's can be undone by the late arrival.
            await s.PostAsync($"/Reservation/Details/{yesterday}", "/Reservation/Transition", ("id", yesterday.ToString()), ("to", StatusNames.Arrived), ("returnTo", "details"));
            Assert.Equal(StatusNames.NoShow, StatusOf(yesterday));
            if (now.AddMinutes(-100).Date == now.Date)
            {
                await s.PostAsync($"/Reservation/Details/{overdue}", "/Reservation/Transition", ("id", overdue.ToString()), ("to", StatusNames.Arrived), ("returnTo", "details"));
                var late = Db(db => db.Reserves.Include(r => r.Status).AsNoTracking().Single(r => r.Id == overdue));
                Assert.Equal(StatusNames.Arrived, late.Status!.Name);
                Assert.Null(late.NoShowAt);
                Assert.False(late.NoShowAuto);
                Assert.NotNull(late.QueueTicket);
            }
        }

        [Fact]
        public async Task Booking_a_patient_who_often_misses_visits_warns_everywhere()
        {
            var (patient, other, doctorId, examId) = Db(db => (TestData.Patient(db), TestData.Patient(db), TestData.Doctor(db).Id, TestData.Exam(db).Id));
            var today = ClinicTime.Today;
            foreach (var monthsAgo in new[] { 1, 3, 6 })
                Visit(patient.Id, doctorId, today.AddMonths(-monthsAgo).AddHours(10), StatusNames.NoShow);
            Visit(patient.Id, doctorId, today.AddMonths(-14).AddHours(10), StatusNames.NoShow); // outside the 12 months
            foreach (var monthsAgo in new[] { 1, 2 })
                Visit(other.Id, doctorId, today.AddMonths(-monthsAgo).AddHours(10), StatusNames.NoShow);

            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var lookup = await s.Client.GetStringAsync($"/Patient/Lookup?pin={patient.PinCode}");
            Assert.Contains("3 dəfə qəbula gəlməyib", System.Text.RegularExpressions.Regex.Unescape(lookup));
            Assert.Contains("\"noShowWarning\":null", await s.Client.GetStringAsync($"/Patient/Lookup?pin={other.PinCode}"));

            var form = await WebSession.TextAsync(await s.Client.GetAsync($"/Reservation/Add?patientId={patient.Id}"));
            Assert.Contains("son 12 ayda 3 dəfə qəbula gəlməyib", form);

            var day = today.AddDays(5);
            var booked = await s.PostAsync($"/Reservation/Add?patientId={patient.Id}", "/Reservation/Add", ("PinCode", patient.PinCode), ("Phone", patient.Phone),
                ("DoctorId", doctorId.ToString()), ("AppointmentDate", day.ToString("yyyy-MM-dd")), ("AppointmentTime", "12:00"), ("ExaminationTypeIds", examId.ToString()));
            Assert.Equal(HttpStatusCode.Redirect, booked.StatusCode);
            var details = await s.FollowAsync(booked);
            Assert.Contains("rezervasiya yaradıldı", details);
            Assert.Contains("3 dəfə qəbula gəlməyib", details);  // the flash
            Assert.Contains("3 dəfə gəlməyib", details);         // the badge

            var otherBooked = await s.FollowAsync(await s.PostAsync($"/Reservation/Add?patientId={other.Id}", "/Reservation/Add", ("PinCode", other.PinCode), ("Phone", other.Phone),
                ("DoctorId", doctorId.ToString()), ("AppointmentDate", day.ToString("yyyy-MM-dd")), ("AppointmentTime", "13:00"), ("ExaminationTypeIds", examId.ToString())));
            Assert.DoesNotContain("qəbula gəlməyib", otherBooked);
            Assert.Contains("2 dəfə gəlməyib", otherBooked); // counted, below the warning

            var list = await WebSession.TextAsync(await s.Client.GetAsync($"/Reservation?period=all&doctorId={doctorId}"));
            Assert.Contains("title=\"3 dəfə gəlməyib\"", list);
            Assert.DoesNotContain("title=\"2 dəfə gəlməyib\"", list);
        }
    }
}
