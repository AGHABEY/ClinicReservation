using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Notifications;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec B2: follow-ups from the record, the «due» list with SMS, and the follow-up discount.</summary>
    [Collection(DbCollection.Name)]
    public class FollowUpHttpTests
    {
        private readonly AppFixture _app;
        public FollowUpHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");

        [Fact]
        public async Task Doctor_sets_a_follow_up_reception_reminds_and_books_it_with_the_discount()
        {
            var doctor = await WebSession.LoginAsync(_app, AppRoles.Doctor);
            string userId;
            using (var scope = _app.Scope())
                userId = (await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByNameAsync("t_hekim"))!.Id;
            var (visitId, patient, doctorId, consult, lab) = Db(db =>
            {
                var me = db.Doctors.FirstOrDefault(d => d.UserId == userId) ?? TestData.Doctor(db);
                me.UserId = userId;
                var consult = TestData.Exam(db);
                consult.Price = 50;
                consult.FollowUpDiscount = true;
                var lab = TestData.Exam(db);
                lab.Price = 20;
                var p = TestData.Patient(db);
                var r = TestData.Reserve(db, p, StatusNames.Arrived, consult);
                r.DoctorId = me.Id;
                r.AppointmentAt = ClinicTime.Now.AddMinutes(-30);
                db.SaveChanges();
                return (r.Id, p, me.Id, consult.Id, lab.Id);
            });

            // The doctor finishes the visit with "control in 10 days" → a plan for reception.
            await doctor.PostAsync($"/Record/Edit?reserveId={visitId}", "/Record/Edit", ("ReserveId", visitId.ToString()),
                ("Diagnosis", "Hipertenziya"), ("FollowUpDays", "10"), ("Finish", "1"));
            var plan = Db(db => db.FollowUps.AsNoTracking().Single(f => f.FromReserveId == visitId));
            Assert.Equal((FollowUpStatus.Due, ClinicTime.Today.AddDays(10), doctorId), (plan.Status, plan.DueDate, plan.DoctorId!.Value));

            // Moving it to 3 days puts it in «Vaxtı çatanlar».
            await doctor.PostAsync($"/Record/Edit?reserveId={visitId}", "/Record/Edit", ("ReserveId", visitId.ToString()),
                ("Diagnosis", "Hipertenziya"), ("FollowUpDays", "3"));
            Assert.Equal(ClinicTime.Today.AddDays(3), Db(db => db.FollowUps.Single(f => f.Id == plan.Id).DueDate));

            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var due = await WebSession.TextAsync(await s.Client.GetAsync($"/FollowUp?doctorId={doctorId}"));
            Assert.Contains(patient.FullName, due);
            Assert.Contains("3 gün sonra", due);

            // One SMS per plan; the bulk button then has nobody left among those.
            Assert.Contains("SMS göndərildi", await s.FollowAsync(await s.PostAsync($"/FollowUp?doctorId={doctorId}", $"/FollowUp/Remind/{plan.Id}")));
            var sms = Db(db => db.SmsMessages.Where(m => m.PatientId == patient.Id && m.Kind == SmsService.KindFollowUp).ToList());
            Assert.Single(sms);
            Assert.Contains(ClinicTime.Today.AddDays(3).ToString("dd.MM.yyyy"), sms[0].Text);
            Assert.NotNull(Db(db => db.FollowUps.Single(f => f.Id == plan.Id).SmsSentAt));

            // Booking from the list: the consultation is free (follow-up), the lab test is not.
            var form = await WebSession.TextAsync(await s.Client.GetAsync($"/Reservation/Add?followUpId={plan.Id}"));
            Assert.Contains("Kontrol qəbulu", form);
            var booked = await s.PostAsync($"/Reservation/Add?followUpId={plan.Id}", "/Reservation/Add", ("FollowUpId", plan.Id.ToString()),
                ("PinCode", patient.PinCode), ("Phone", patient.Phone), ("DoctorId", doctorId.ToString()),
                ("AppointmentDate", D(ClinicTime.Today.AddDays(3))), ("AppointmentTime", "10:00"),
                ("ExaminationTypeIds", consult.ToString()), ("ExaminationTypeIds", lab.ToString()));
            var details = await s.FollowAsync(booked);
            Assert.Contains("Kontrol qəbulu (pulsuz): 50,00", details);
            var newId = Db(db => db.FollowUps.Single(f => f.Id == plan.Id).ReserveId!.Value);
            Assert.Equal(FollowUpStatus.Booked, Db(db => db.FollowUps.Single(f => f.Id == plan.Id).Status));
            var lines = Db(db => db.ReserveExaminationTypes.AsNoTracking().Where(x => x.ReserveId == newId).ToList());
            var c = lines.Single(x => x.ExaminationTypeId == consult);
            var l = lines.Single(x => x.ExaminationTypeId == lab);
            Assert.Equal((50m, 50m, 0m), (c.ListPrice, c.Discount, c.Price));
            Assert.Equal((20m, 0m, 20m), (l.ListPrice, l.Discount, l.Price));
            Assert.Equal(visitId, Db(db => db.Reserves.Single(r => r.Id == newId).FollowUpOfId));
            Assert.Contains("Endirim", details);
        }

        [Fact]
        public async Task No_discount_with_another_doctor_or_after_the_window_and_plans_can_be_closed()
        {
            var (patient, doctorId, otherDoctor, consult) = Db(db =>
            {
                var d = TestData.Doctor(db);
                var other = TestData.Doctor(db);
                var e = TestData.Exam(db);
                e.Price = 40;
                e.FollowUpDiscount = true;
                var p = TestData.Patient(db);
                var old = TestData.Reserve(db, p, StatusNames.Completed, e);
                old.DoctorId = d.Id;
                old.AppointmentAt = ClinicTime.Today.AddDays(-20).AddHours(10); // outside the 14-day window
                db.SaveChanges();
                return (p, d.Id, other.Id, e.Id);
            });
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            async Task<decimal> BookPrice(int doc, string time)
            {
                var r = await s.PostAsync("/Reservation/Add", "/Reservation/Add", ("PinCode", patient.PinCode), ("Phone", patient.Phone),
                    ("DoctorId", doc.ToString()), ("AppointmentDate", D(ClinicTime.Today.AddDays(1))), ("AppointmentTime", time), ("ExaminationTypeIds", consult.ToString()));
                Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
                var id = int.Parse(r.Headers.Location!.ToString().Split('/').Last());
                return Db(db => db.ReserveExaminationTypes.Single(x => x.ReserveId == id).Price);
            }
            Assert.Equal(40m, await BookPrice(doctorId, "10:00"));

            // A completed visit 5 days ago: same doctor → free, another doctor → full price.
            Db(db =>
            {
                var recent = TestData.Reserve(db, db.Patients.Find(patient.Id)!, StatusNames.Completed, db.ExaminationTypes.Find(consult)!);
                recent.DoctorId = doctorId;
                recent.AppointmentAt = ClinicTime.Today.AddDays(-5).AddHours(10);
                return db.SaveChanges();
            });
            Assert.Equal(0m, await BookPrice(doctorId, "11:00"));
            Assert.Equal(40m, await BookPrice(otherDoctor, "11:00"));

            // A plan can be taken off the list, with a note.
            var planId = Db(db =>
            {
                var from = db.Reserves.First(r => r.PatientId == patient.Id && r.Status!.Name == StatusNames.Completed);
                var f = new FollowUp { FromReserveId = from.Id, PatientId = patient.Id, DoctorId = otherDoctor, DueDate = ClinicTime.Today, CreatedAt = ClinicTime.Now };
                db.FollowUps.Add(f);
                db.SaveChanges();
                return f.Id;
            });
            await s.PostAsync("/FollowUp", $"/FollowUp/Close/{planId}", ("note", "Başqa şəhərə köçüb"));
            Assert.Equal(FollowUpStatus.Closed, Db(db => db.FollowUps.Single(f => f.Id == planId).Status));
            Assert.Contains("Başqa şəhərə köçüb", await WebSession.TextAsync(await s.Client.GetAsync("/FollowUp?tab=Closed")));
        }
    }
}
