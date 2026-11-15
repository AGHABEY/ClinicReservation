using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec A2: a course of sessions — plan, alternatives for busy days, booking, cancelling the rest.</summary>
    [Collection(DbCollection.Name)]
    public class CourseBookingHttpTests
    {
        private readonly AppFixture _app;
        public CourseBookingHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Course_is_planned_then_booked_and_the_rest_can_be_cancelled()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var (doctorId, examId) = Db(db => (TestData.Doctor(db).Id, TestData.Exam(db).Id));
            // Start on the next Monday; Monday and Wednesday sessions at 10:00.
            var start = ClinicTime.Today.AddDays(1);
            while (start.DayOfWeek != DayOfWeek.Monday) start = start.AddDays(1);
            // Someone already has the doctor at 10:00 on the second Monday.
            Db(db =>
            {
                var other = TestData.Reserve(db, TestData.Patient(db), StatusNames.Confirmed, TestData.Exam(db));
                other.DoctorId = doctorId;
                other.AppointmentAt = start.AddDays(7).AddHours(10);
                return db.SaveChanges();
            });
            var pin = Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
            (string, string)[] Form(string step) => new[]
            {
                ("PinCode", pin), ("Name", "Kurs"), ("Surname", "Xəstə"), ("Phone", "050 222 33 44"), ("Birthday", "1970-03-03"),
                ("DoctorId", doctorId.ToString()), ("AppointmentDate", start.ToString("yyyy-MM-dd")), ("AppointmentTime", "10:00"),
                ("ExaminationTypeIds", examId.ToString()), ("SeriesCount", "4"), ("SeriesDays", "Monday"), ("SeriesDays", "Wednesday"), ("Step", step)
            };

            // First submit only shows the plan: nothing is booked yet.
            var preview = await s.PostAsync("/Reservation/Add", "/Reservation/Add", Form(""));
            var plan = await WebSession.TextAsync(preview);
            Assert.Contains("Başqa saat", plan);  // the busy Monday got a nearby time
            Assert.Contains("Kursu yarat", plan);
            Assert.False(Db(db => db.Patients.Any(p => p.PinCode == pin)));

            var booked = await s.PostAsync("/Reservation/Add", "/Reservation/Add", Form("confirm"));
            Assert.Contains("4 seanslıq kurs yaradıldı", await s.FollowAsync(booked));
            var sessions = Db(db => db.Reserves.Include(r => r.Series).Include(r => r.Patient)
                .Where(r => r.Patient.PinCode == pin).OrderBy(r => r.AppointmentAt).ToList());
            Assert.Equal(4, sessions.Count);
            Assert.All(sessions, r => Assert.Equal(sessions[0].SeriesId, r.SeriesId));
            Assert.Equal(new[] { 1, 2, 3, 4 }, sessions.Select(r => r.SeriesNo!.Value));
            Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Wednesday }, sessions.Select(r => r.AppointmentAt.DayOfWeek));
            Assert.NotEqual(TimeSpan.FromHours(10), sessions[2].AppointmentAt.TimeOfDay); // moved off the busy 10:00

            // Cancelling from the 2nd session cancels the 2nd, 3rd and 4th; the 1st stays.
            var second = sessions[1].Id;
            var cancel = await s.PostAsync($"/Reservation/Details/{second}", "/Reservation/Cancel", ("Id", second.ToString()),
                ("ReasonId", Db(TestData.ReasonId).ToString()), ("Series", "true"));
            Assert.Contains("Kursun 3 seansı ləğv edildi", await s.FollowAsync(cancel));
            var statuses = Db(db => db.Reserves.Include(r => r.Status).Where(r => r.SeriesId == sessions[0].SeriesId)
                .OrderBy(r => r.AppointmentAt).Select(r => r.Status!.Name).ToList());
            Assert.Equal(new[] { StatusNames.Waiting, StatusNames.Cancelled, StatusNames.Cancelled, StatusNames.Cancelled }, statuses);
        }
    }
}
