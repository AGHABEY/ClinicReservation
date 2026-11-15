using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>The doctor's note: who may write it, how it moves the visit, ordering labs from it.</summary>
    [Collection(DbCollection.Name)]
    public class MedicalRecordHttpTests
    {
        private readonly AppFixture _app;
        public MedicalRecordHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Own_patient_only_note_starts_and_finishes_the_visit()
        {
            var doctor = await WebSession.LoginAsync(_app, AppRoles.Doctor);
            string userId;
            using (var scope = _app.Scope())
                userId = (await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByNameAsync("t_hekim"))!.Id;

            var (mine, other, labExamId) = Db(db =>
            {
                // The test doctor's login is linked to one doctor profile (reused across tests).
                var me = db.Doctors.FirstOrDefault(d => d.UserId == userId) ?? TestData.Doctor(db);
                me.UserId = userId;
                var someoneElse = TestData.Doctor(db);
                var labExam = TestData.Exam(db);
                labExam.LabTests.Add(new ExaminationTypeLabTest { LabTest = new LabTest { Code = TestData.Unique("E"), Name = "Test" } });
                db.SaveChanges();
                Reserve Visit(Doctor d)
                {
                    var r = TestData.Reserve(db, TestData.Patient(db), StatusNames.Arrived, TestData.Exam(db));
                    r.DoctorId = d.Id;
                    db.SaveChanges();
                    return r;
                }
                return (Visit(me).Id, Visit(someoneElse).Id, labExam.Id);
            });

            var refused = await doctor.Client.GetAsync($"/Record/Edit?reserveId={other}");
            Assert.Contains("başqa həkimə aiddir", await doctor.FollowAsync(refused));

            // Finishing needs a diagnosis; nothing is stored without it.
            var noDiagnosis = await doctor.PostAsync($"/Record/Edit?reserveId={mine}", "/Record/Edit", ("ReserveId", mine.ToString()), ("Complaint", "Baş ağrısı"), ("Finish", "1"));
            Assert.Contains("Qəbulu bitirmək üçün diaqnozu yazın", await WebSession.TextAsync(noDiagnosis));
            Assert.False(Db(db => db.MedicalRecords.Any(m => m.ReserveId == mine)));

            // Saving the note starts the visit and records allergies on the patient card.
            var saved = await doctor.PostAsync($"/Record/Edit?reserveId={mine}", "/Record/Edit", ("ReserveId", mine.ToString()), ("Complaint", "Baş ağrısı"), ("Allergies", "Penisillin"));
            Assert.Contains("Tibbi qeyd saxlanıldı", await doctor.FollowAsync(saved));
            Assert.Equal(StatusNames.InProgress, Db(db => db.Reserves.Include(r => r.Status).Single(r => r.Id == mine).Status!.Name));
            Assert.Equal("Penisillin", Db(db => db.Reserves.Include(r => r.Patient).Single(r => r.Id == mine).Patient.Allergies));

            // Labs ordered during the visit are added to it at today's price.
            var ordered = await doctor.PostAsync($"/Record/Edit?reserveId={mine}", "/Record/OrderLab", ("reserveId", mine.ToString()), ("examinationTypeIds", labExamId.ToString()));
            Assert.Contains("Analiz təyin edildi", await doctor.FollowAsync(ordered));
            Assert.True(Db(db => db.ReserveExaminationTypes.Any(x => x.ReserveId == mine && x.ExaminationTypeId == labExamId)));

            var finished = await doctor.PostAsync($"/Record/Edit?reserveId={mine}", "/Record/Edit", ("ReserveId", mine.ToString()),
                ("Complaint", "Baş ağrısı"), ("IcdCode", "g44.2"), ("Diagnosis", "Gərginlik tipli baş ağrısı"), ("Finish", "1"));
            var show = await doctor.FollowAsync(finished);
            Assert.Contains("qəbul tamamlandı", show);
            Assert.Contains("G44.2", show);
            Assert.Equal(StatusNames.Completed, Db(db => db.Reserves.Include(r => r.Status).Single(r => r.Id == mine).Status!.Name));
        }
    }
}
