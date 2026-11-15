using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec B1: the doctor refers, reception books, completion carries it out; failed or expired ones show as not done.</summary>
    [Collection(DbCollection.Name)]
    public class ReferralHttpTests
    {
        private readonly AppFixture _app;
        public ReferralHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");

        [Fact]
        public async Task Doctor_refers_reception_books_and_completing_the_visit_carries_it_out()
        {
            var doctor = await WebSession.LoginAsync(_app, AppRoles.Doctor);
            string userId;
            using (var scope = _app.Scope())
                userId = (await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByNameAsync("t_hekim"))!.Id;
            var (visitId, patient, cardioDept, cardiologist, examId) = Db(db =>
            {
                var me = db.Doctors.FirstOrDefault(d => d.UserId == userId) ?? TestData.Doctor(db);
                me.UserId = userId;
                var dept = TestData.Department(db);
                var cardio = TestData.Doctor(db, dept.Id);
                var p = TestData.Patient(db);
                var r = TestData.Reserve(db, p, StatusNames.Arrived, TestData.Exam(db));
                r.DoctorId = me.Id;
                db.SaveChanges();
                return (r.Id, p, dept.Id, cardio.Id, TestData.Exam(db).Id);
            });

            var page = await WebSession.TextAsync(await doctor.Client.GetAsync($"/Record/Edit?reserveId={visitId}"));
            Assert.Contains("Göndəriş yaz", page);

            // Nowhere to send / no reason → refused.
            var empty = await doctor.FollowAsync(await doctor.PostAsync($"/Record/Edit?reserveId={visitId}", "/Record/Refer", ("FromReserveId", visitId.ToString())));
            Assert.Contains("Hara göndərilir", empty);

            var sent = await doctor.FollowAsync(await doctor.PostAsync($"/Record/Edit?reserveId={visitId}", "/Record/Refer",
                ("FromReserveId", visitId.ToString()), ("ToDepartmentId", cardioDept.ToString()), ("Priority", "Urgent"),
                ("IcdCode", "i10"), ("Reason", "Arterial hipertenziya, EXO-KQ"), ("ValidDays", "14")));
            Assert.Contains("Göndəriş yazıldı", sent);
            var referral = Db(db => db.Referrals.AsNoTracking().Single(r => r.FromReserveId == visitId));
            Assert.Equal((ReferralStatus.Open, ReferralPriority.Urgent, "I10", cardioDept), (referral.Status, referral.Priority, referral.IcdCode, referral.ToDepartmentId!.Value));
            Assert.Equal(ClinicTime.Today.AddDays(14), referral.ValidUntil);
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/Referral/Print/{referral.Id}")).StatusCode);

            // Reception sees it waiting, and books it: the form is filled and the source is «Göndəriş».
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var list = await WebSession.TextAsync(await s.Client.GetAsync("/Referral"));
            Assert.Contains(patient.FullName, list);
            Assert.Contains("Təcili", list);
            var form = await WebSession.TextAsync(await s.Client.GetAsync($"/Reservation/Add?referralId={referral.Id}"));
            Assert.Contains("Göndəriş üzrə", form);
            Assert.Contains($"value=\"{patient.PinCode}\"", form);

            var day = ClinicTime.Today.AddDays(2);
            var booked = await s.PostAsync($"/Reservation/Add?referralId={referral.Id}", "/Reservation/Add",
                ("ReferralId", referral.Id.ToString()), ("Source", "Referral"), ("PinCode", patient.PinCode), ("Phone", patient.Phone),
                ("DoctorId", cardiologist.ToString()), ("AppointmentDate", D(day)), ("AppointmentTime", "10:00"), ("ExaminationTypeIds", examId.ToString()));
            var details = await s.FollowAsync(booked);
            Assert.Contains("Göndəriş üzrə yazılıb", details);
            var reserveId = Db(db => db.Referrals.Single(r => r.Id == referral.Id).ReserveId!.Value);
            Assert.Equal(ReserveSource.Referral, Db(db => db.Reserves.Single(r => r.Id == reserveId).Source));
            Assert.Contains(patient.FullName, await WebSession.TextAsync(await s.Client.GetAsync("/Referral?state=Booked")));
            Assert.DoesNotContain(patient.FullName, await WebSession.TextAsync(await s.Client.GetAsync("/Referral")));

            // Cancelled booking → waiting again (to be rebooked).
            await s.PostAsync($"/Reservation/Details/{reserveId}", "/Reservation/Cancel", ("Id", reserveId.ToString()), ("ReasonId", Db(TestData.ReasonId).ToString()));
            Assert.Contains(patient.FullName, await WebSession.TextAsync(await s.Client.GetAsync("/Referral")));

            // Booking the cardiologist again links it by itself; completing the visit carries it out.
            var again = await s.PostAsync("/Reservation/Add", "/Reservation/Add", ("PinCode", patient.PinCode), ("Phone", patient.Phone),
                ("DoctorId", cardiologist.ToString()), ("AppointmentDate", D(day)), ("AppointmentTime", "11:00"), ("ExaminationTypeIds", examId.ToString()));
            Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
            var second = Db(db => db.Referrals.Single(r => r.Id == referral.Id).ReserveId!.Value);
            Assert.NotEqual(reserveId, second);
            Db(db =>
            {
                var v = db.Reserves.Single(r => r.Id == second);
                v.Status = db.ReserveStatuses.Single(x => x.Name == StatusNames.Completed);
                return db.SaveChanges();
            });
            Assert.Contains(patient.FullName, await WebSession.TextAsync(await s.Client.GetAsync("/Referral?state=Done")));
        }

        [Fact]
        public async Task Outside_referrals_are_entered_by_reception_and_expired_or_cancelled_ones_are_not_done()
        {
            var (patient, deptId) = Db(db => (TestData.Patient(db), TestData.Department(db).Id));
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);

            var bad = await WebSession.TextAsync(await s.PostAsync("/Referral/Add", "/Referral/Add", ("PinCode", "ZZZ0000"), ("ToDepartmentId", deptId.ToString()), ("Reason", "x")));
            Assert.Contains("Bu FİN ilə xəstə kartı tapılmadı", bad);
            Assert.Contains("Göndərən müəssisəni", bad);

            var ok = await s.PostAsync("/Referral/Add", "/Referral/Add", ("PinCode", patient.PinCode), ("ExternalFrom", "5 saylı poliklinika"),
                ("ToDepartmentId", deptId.ToString()), ("Reason", "Konsultasiya"), ("ValidDays", "10"));
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            var id = Db(db => db.Referrals.Where(r => r.PatientId == patient.Id).Select(r => r.Id).Single());
            Assert.Contains("5 saylı poliklinika", await WebSession.TextAsync(await s.Client.GetAsync("/Referral")));

            // Expired without a visit → «İcra olunmayıb».
            Db(db => { db.Referrals.Single(r => r.Id == id).ValidUntil = ClinicTime.Today.AddDays(-1); return db.SaveChanges(); });
            Assert.Contains(patient.FullName, await WebSession.TextAsync(await s.Client.GetAsync("/Referral?state=NotDone")));
            Assert.DoesNotContain(patient.FullName, await WebSession.TextAsync(await s.Client.GetAsync("/Referral")));

            // Cancelling needs a reason.
            Db(db => { db.Referrals.Single(r => r.Id == id).ValidUntil = ClinicTime.Today.AddDays(5); return db.SaveChanges(); });
            Assert.Contains("Ləğv səbəbini yazın", await s.FollowAsync(await s.PostAsync("/Referral", $"/Referral/Cancel/{id}", ("reason", ""))));
            await s.PostAsync("/Referral", $"/Referral/Cancel/{id}", ("reason", "Xəstə imtina etdi"));
            Assert.Equal(ReferralStatus.Cancelled, Db(db => db.Referrals.Single(r => r.Id == id).Status));
            Assert.Contains("Xəstə imtina etdi", await WebSession.TextAsync(await s.Client.GetAsync("/Referral?state=NotDone")));
        }
    }
}
