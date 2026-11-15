using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>A hospital stay through the forms: admit, notes, orders, medication round, discharge.</summary>
    [Collection(DbCollection.Name)]
    public class InpatientHttpTests
    {
        private readonly AppFixture _app;
        public InpatientHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Whole_stay_from_admission_to_epicrisis()
        {
            var reception = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var doctor = await WebSession.LoginAsync(_app, AppRoles.Doctor);
            var nurse = await WebSession.LoginAsync(_app, AppRoles.Nurse);

            // A department with its own store holding the drug, and a women's ward in it.
            var (patient, ward, drug, store) = Db(db =>
            {
                var dept = TestData.Department(db);
                var store = TestData.Warehouse(db, dept.Id);
                var drug = TestData.Item(db, StockItemKind.Medicine, "ampul");
                return (TestData.Patient(db, Gender.Male), TestData.Ward(db, 2, 50, dept.Id, WardGender.Female), drug, store);
            });
            using (var scope = _app.Scope())
                scope.ServiceProvider.GetRequiredService<StockService>().Receive(store.Id, null, null, ClinicTime.Today, null,
                    new[] { new StockService.ReceiptLine(drug.Id, 10, 1, "X", ClinicTime.Today.AddYears(1)) }, "test");

            // Admission: unknown FİN, then a man into the women's ward (allowed, with a warning).
            var unknown = await reception.PostAsync("/Inpatient/Admit", "/Inpatient/Admit", ("PatientPin", "ZZZ9999"), ("BedId", ward.Beds[0].Id.ToString()), ("Diagnosis", "x"), ("Type", "Planned"));
            Assert.Contains("ZZZ9999 FİN-li xəstə tapılmadı", await WebSession.TextAsync(unknown));

            var admitted = await reception.PostAsync("/Inpatient/Admit", "/Inpatient/Admit",
                ("PatientPin", patient.PinCode.ToLowerInvariant()), ("BedId", ward.Beds[0].Id.ToString()), ("Type", "Emergency"),
                ("IcdCode", "i10"), ("Diagnosis", "Hipertoniya"));
            var casePage = await reception.FollowAsync(admitted);
            Assert.Contains("yerləşdirildi", casePage);
            Assert.Contains("qadın palatasıdır", casePage);
            var id = int.Parse(admitted.Headers.Location!.ToString().Split('/').Last());
            var url = $"/Inpatient/Details/{id}";

            // Nurse: two form problems are reported together; then vitals are saved.
            var bad = await nurse.PostAsync(url, "/Inpatient/Observe", ("AdmissionId", id.ToString()), ("Kind", "Nurse"), ("Temperature", "50"), ("BpSystolic", "120"));
            var badText = await WebSession.TextAsync(bad);
            Assert.Contains("Temperatur 30–45", badText);
            Assert.Contains("Təzyiqin hər iki rəqəmini", badText);
            var vitals = await nurse.PostAsync(url, "/Inpatient/Observe", ("AdmissionId", id.ToString()), ("Kind", "Nurse"), ("Temperature", "37.8"), ("BpSystolic", "160"), ("BpDiastolic", "100"));
            Assert.Contains("Qeyd saxlanıldı", await nurse.FollowAsync(vitals));

            // Nurse may not write the doctor's note; the doctor may.
            var denied = await nurse.PostAsync(url, "/Inpatient/Observe", ("AdmissionId", id.ToString()), ("Kind", "Doctor"), ("Text", "x"));
            Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
            Assert.Contains("Qeyd saxlanıldı", await doctor.FollowAsync(await doctor.PostAsync(url, "/Inpatient/Observe", ("AdmissionId", id.ToString()), ("Kind", "Doctor"), ("Text", "Vəziyyəti orta ağır"))));

            // Orders: a wrong time is refused; a stock drug due now is ordered.
            var badTime = await doctor.PostAsync(url, "/Inpatient/Order", ("AdmissionId", id.ToString()), ("DrugName", "Kaptopril"), ("Times", "25:00"));
            Assert.Contains("«25:00» düzgün vaxt deyil", await WebSession.TextAsync(badTime));
            // The current minute (not the hour): a dose due before the hour turned would fall outside the order.
            var hour = ClinicTime.Now.ToString("HH:mm");
            var ordered = await doctor.PostAsync(url, "/Inpatient/Order", ("AdmissionId", id.ToString()), ("StockItemId", drug.Id.ToString()),
                ("Dose", "4 mq"), ("QuantityPerDose", "1"), ("Route", "Venadaxili"), ("Times", hour), ("Days", "3"));
            Assert.Contains("təyin edildi", await doctor.FollowAsync(ordered));
            var orderId = Db(db => db.MedicationOrders.Single(o => o.AdmissionId == id).Id);

            // Round: the nurse gives the dose once; the drug leaves the department store.
            var slot = ClinicTime.Today.ToString("yyyy-MM-dd") + "T" + hour;
            var given = await nurse.PostAsync(url + "?tab=mar", "/Inpatient/Dose", ("id", id.ToString()), ("orderId", orderId.ToString()), ("slot", slot), ("status", "Given"));
            Assert.Contains("— verildi", await nurse.FollowAsync(given));
            var again = await nurse.PostAsync(url + "?tab=mar", "/Inpatient/Dose", ("id", id.ToString()), ("orderId", orderId.ToString()), ("slot", slot), ("status", "Given"));
            Assert.Contains("Bu doza artıq qeyd olunub", await nurse.FollowAsync(again));
            Assert.Equal(9, Db(db => db.StockBatches.Where(b => b.WarehouseId == store.Id).Sum(b => b.Quantity)));

            // Records exist, so the stay can't be cancelled — only discharged.
            var cancel = await reception.PostAsync(url, "/Inpatient/Cancel", ("id", id.ToString()), ("reason", "səhv"));
            Assert.Contains("ləğv etmək olmaz", await reception.FollowAsync(cancel));

            var noOutcome = await doctor.PostAsync($"/Inpatient/Discharge/{id}", "/Inpatient/Discharge", ("AdmissionId", id.ToString()), ("Diagnosis", "x"));
            Assert.Contains("Nəticəni seçin", await WebSession.TextAsync(noOutcome));
            var discharged = await doctor.PostAsync($"/Inpatient/Discharge/{id}", "/Inpatient/Discharge", ("AdmissionId", id.ToString()),
                ("Outcome", "Improved"), ("IcdCode", "I10"), ("Diagnosis", "Hipertoniya, kriz"), ("Recommendations", "Kardioloq"));
            var epicrisis = await doctor.FollowAsync(discharged);
            Assert.Contains("EPİKRİZ", epicrisis);
            Assert.Contains("Yaxşılaşma", epicrisis);

            var stay = Db(db => db.Admissions.AsNoTracking().Single(a => a.Id == id));
            Assert.Equal(AdmissionStatus.Discharged, stay.Status);
            Assert.Null(stay.BedId);
            Assert.True(Db(db => db.AuditLogs.Count(a => a.EntityType == "Stasionar" && a.EntityId == id.ToString())) >= 6);
        }

        [Fact]
        public async Task Ward_settings_validate_and_add_beds()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var none = await s.PostAsync("/Ward", "/Ward/Save", ("id", "0"), ("name", "P-x"), ("gender", "Mixed"), ("dailyPrice", "40"), ("sortOrder", "1"), ("bedCount", "0"));
            Assert.Contains("Çarpayı sayı 1–30", await s.FollowAsync(none));

            var name = TestData.Unique("P");
            var ok = await s.PostAsync("/Ward", "/Ward/Save", ("id", "0"), ("name", name), ("gender", "Male"), ("dailyPrice", "45.5"), ("sortOrder", "1"), ("bedCount", "3"));
            Assert.Contains("3 çarpayı ilə əlavə edildi", await s.FollowAsync(ok));
            var wardId = Db(db => db.Wards.Single(w => w.Name == name).Id);

            var bed = await s.PostAsync("/Ward", "/Ward/AddBed", ("wardId", wardId.ToString()));
            Assert.Contains("4 nömrəli çarpayı əlavə edildi", await s.FollowAsync(bed));
            Assert.Equal(45.5m, Db(db => db.Wards.Single(w => w.Id == wardId).DailyPrice));
        }
    }
}
