using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec A1: service length, shared devices, booking source.</summary>
    [Collection(DbCollection.Name)]
    public class BookingResourcesHttpTests
    {
        private readonly AppFixture _app;
        public BookingResourcesHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string Pin() => Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

        private static (string, string)[] Booking(int doctorId, string date, string time, params int[] exams) =>
            new[] { ("PinCode", Pin()), ("Name", "Test"), ("Surname", "Xəstə"), ("Phone", "050 123 45 67"), ("Birthday", "1985-01-01"),
                     ("DoctorId", doctorId.ToString()), ("AppointmentDate", date), ("AppointmentTime", time), ("Source", "Phone") }
                .Concat(exams.Select(e => ("ExaminationTypeIds", e.ToString()))).ToArray();

        [Fact]
        public async Task Long_services_take_several_slots_and_a_device_is_booked_once()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var (d1, d2, usm, consult) = Db(db =>
            {
                var eq = new Equipment { Name = TestData.Unique("USM ") };
                db.Equipment.Add(eq);
                db.SaveChanges();
                var usm = TestData.Exam(db);
                usm.DurationMinutes = 45; // 30-min grid → takes 60 minutes
                usm.EquipmentId = eq.Id;
                var consult = TestData.Exam(db);
                db.SaveChanges();
                return (TestData.Doctor(db).Id, TestData.Doctor(db).Id, usm.Id, consult.Id);
            });
            var date = ClinicTime.Today.AddDays(2).ToString("yyyy-MM-dd");

            // The slot list for the USM shows its length and device.
            var json = await s.Client.GetStringAsync($"/Reservation/Slots?doctorId={d1}&date={date}&examTypeIds={usm}");
            Assert.Contains("\"minutes\":60", json);

            var first = await s.PostAsync("/Reservation/Add", "/Reservation/Add", Booking(d1, date, "10:00", usm));
            Assert.Contains("rezervasiya yaradıldı", await s.FollowAsync(first));
            var r = Db(db => db.Reserves.OrderByDescending(x => x.Id).First(x => x.DoctorId == d1));
            Assert.Equal(60, r.DurationMinutes);
            Assert.NotNull(r.EquipmentId);
            Assert.Equal(ReserveSource.Phone, r.Source);

            // Same doctor at 10:30 is inside the 60-minute visit.
            var overlap = await s.PostAsync("/Reservation/Add", "/Reservation/Add", Booking(d1, date, "10:30", consult));
            Assert.Contains("Bu saat artıq doludur", await WebSession.TextAsync(overlap));

            // Another doctor is free at 10:30, but the device isn't.
            var device = await s.PostAsync("/Reservation/Add", "/Reservation/Add", Booking(d2, date, "10:30", usm));
            Assert.Contains("məşğuldur", await WebSession.TextAsync(device));

            // Without the device the other doctor can see the patient then; with it, from 11:00.
            Assert.Contains("rezervasiya yaradıldı", await s.FollowAsync(await s.PostAsync("/Reservation/Add", "/Reservation/Add", Booking(d2, date, "10:30", consult))));
            Assert.Contains("rezervasiya yaradıldı", await s.FollowAsync(await s.PostAsync("/Reservation/Add", "/Reservation/Add", Booking(d2, date, "11:00", usm))));
        }
    }
}
