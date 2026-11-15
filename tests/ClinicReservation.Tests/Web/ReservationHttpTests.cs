using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Booking through the forms: validation, double booking, patient card rules, edit, statuses, cancel.</summary>
    [Collection(DbCollection.Name)]
    public class ReservationHttpTests
    {
        private readonly AppFixture _app;
        public ReservationHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string Pin() => Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

        [Fact]
        public async Task Booking_editing_statuses_and_cancel()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var (doctor, exam1, exam2) = Db(db => (TestData.Doctor(db), TestData.Exam(db), TestData.Exam(db)));
            var date = ClinicTime.Today.AddDays(1).ToString("yyyy-MM-dd");
            var pin = Pin();

            // All problems of the form are shown at once, nothing is stored.
            var bad = await s.PostAsync("/Reservation/Add", "/Reservation/Add", ("PinCode", pin), ("Name", ""), ("Surname", "Əliyeva"),
                ("Phone", "abc"), ("Birthday", "1990-02-03"), ("DoctorId", doctor.Id.ToString()), ("AppointmentDate", date), ("AppointmentTime", "10:00"));
            var badText = await WebSession.TextAsync(bad);
            Assert.Contains("Ad daxil edilməlidir", badText);
            Assert.Contains("Telefon nömrəsi düzgün deyil", badText);
            Assert.Contains("Ən azı bir müayinə növü seçin", badText);
            Assert.False(Db(db => db.Patients.Any(p => p.PinCode == pin)));

            var ok = await s.PostAsync("/Reservation/Add", "/Reservation/Add", ("PinCode", pin.ToLowerInvariant()), ("Name", "Aysel"), ("Surname", "Əliyeva"),
                ("Phone", "050 123 45 67"), ("Birthday", "1990-02-03"), ("DoctorId", doctor.Id.ToString()), ("AppointmentDate", date), ("AppointmentTime", "10:00"),
                ("ExaminationTypeIds", exam1.Id.ToString()));
            Assert.Contains("üçün rezervasiya yaradıldı", await s.FollowAsync(ok));
            var id = int.Parse(ok.Headers.Location!.ToString().Split('/').Last());

            // The same doctor and time can't be booked twice; an existing card keeps its name, only the phone changes.
            var twice = await s.PostAsync("/Reservation/Add", "/Reservation/Add", ("PinCode", pin), ("Name", "Başqa"), ("Surname", "Ad"),
                ("Phone", "055 999 88 77"), ("DoctorId", doctor.Id.ToString()), ("AppointmentDate", date), ("AppointmentTime", "10:00"),
                ("ExaminationTypeIds", exam1.Id.ToString()));
            Assert.Contains("Bu saat artıq doludur", await WebSession.TextAsync(twice));
            var second = await s.PostAsync("/Reservation/Add", "/Reservation/Add", ("PinCode", pin), ("Name", "Başqa"), ("Surname", "Ad"),
                ("Phone", "055 999 88 77"), ("DoctorId", doctor.Id.ToString()), ("AppointmentDate", date), ("AppointmentTime", "11:00"),
                ("ExaminationTypeIds", exam2.Id.ToString()));
            Assert.Contains("rezervasiya yaradıldı", await s.FollowAsync(second));
            var patient = Db(db => db.Patients.Single(p => p.PinCode == pin));
            Assert.Equal("Aysel", patient.Name);
            Assert.Equal("055 999 88 77", patient.Phone);

            // Edit: a kept examination keeps its booked price even after the catalog price changes.
            Db(db => db.ExaminationTypes.Where(e => e.Id == exam1.Id).ExecuteUpdate(u => u.SetProperty(e => e.Price, 99m)));
            var edit = await s.PostAsync($"/Reservation/Edit/{id}", "/Reservation/Edit", ("Id", id.ToString()), ("DoctorId", doctor.Id.ToString()),
                ("AppointmentDate", date), ("AppointmentTime", "10:00"), ("ExaminationTypeIds", exam1.Id.ToString()), ("ExaminationTypeIds", exam2.Id.ToString()));
            Assert.Contains("Rezervasiya yeniləndi", await s.FollowAsync(edit));
            var prices = Db(db => db.ReserveExaminationTypes.Where(x => x.ReserveId == id).ToDictionary(x => x.ExaminationTypeId, x => x.Price));
            Assert.Equal(exam1.Price, prices[exam1.Id]);
            Assert.Equal(exam2.Price, prices[exam2.Id]);

            // Statuses: check-in gives a queue ticket; a completed visit can't go back or be cancelled.
            foreach (var to in new[] { StatusNames.Confirmed, StatusNames.Arrived, StatusNames.InProgress, StatusNames.Completed })
            {
                var t = await s.PostAsync($"/Reservation/Details/{id}", "/Reservation/Transition", ("id", id.ToString()), ("to", to), ("returnTo", "details"));
                Assert.DoesNotContain("keçmək olmaz", await s.FollowAsync(t));
            }
            var r = Db(db => db.Reserves.Include(x => x.Status).Single(x => x.Id == id));
            Assert.Equal(StatusNames.Completed, r.Status!.Name);
            Assert.False(string.IsNullOrEmpty(r.QueueTicket));
            var back = await s.PostAsync($"/Reservation/Details/{id}", "/Reservation/Transition", ("id", id.ToString()), ("to", StatusNames.Confirmed), ("returnTo", "details"));
            Assert.Contains("keçmək olmaz", await s.FollowAsync(back));
            var cancelDone = await s.PostAsync($"/Reservation/Details/{id}", "/Reservation/Cancel", ("Id", id.ToString()), ("Reason", "x"));
            Assert.Contains("ləğv etmək olmaz", await s.FollowAsync(cancelDone));

            // The second booking is still waiting and can be cancelled.
            var secondId = int.Parse(second.Headers.Location!.ToString().Split('/').Last());
            var cancel = await s.PostAsync($"/Reservation/Details/{secondId}", "/Reservation/Cancel", ("Id", secondId.ToString()), ("Reason", "Xəstə zəng etdi"));
            Assert.Contains("Rezervasiya ləğv edildi", await s.FollowAsync(cancel));
            Assert.Equal("Xəstə zəng etdi", Db(db => db.Reserves.Single(x => x.Id == secondId).CancelReason));
        }
    }
}
