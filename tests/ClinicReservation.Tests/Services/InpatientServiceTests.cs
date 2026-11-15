using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Services
{
    [Collection(DbCollection.Name)]
    public class InpatientServiceTests
    {
        private readonly AppFixture _app;
        public InpatientServiceTests(AppFixture app) => _app = app;

        private (IServiceScope Scope, AppDbContext Db, InpatientService Svc) Open()
        {
            var scope = _app.Scope();
            return (scope, scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<InpatientService>());
        }

        private static Admission Load(AppDbContext db, int id) =>
            db.Admissions.Include(a => a.Patient).Include(a => a.Stays).First(a => a.Id == id);

        [Fact]
        public void A_bed_and_a_patient_hold_one_active_stay_each()
        {
            var (scope, db, svc) = Open();
            using var scopeLifetime = scope;
            var ward = TestData.Ward(db, 2);
            var p1 = TestData.Patient(db);
            var p2 = TestData.Patient(db);

            var first = svc.Admit(p1.Id, ward.Beds[0].Id, null, AdmissionType.Planned, "I10", "Hipertenziya", null, null, "test");
            Assert.True(first.Ok, first.Error);
            Assert.Matches(@"^ST-\d{5}$", first.Admission!.Number);

            Assert.False(svc.Admit(p2.Id, ward.Beds[0].Id, null, AdmissionType.Planned, null, "x", null, null, "test").Ok); // bed taken
            Assert.False(svc.Admit(p1.Id, ward.Beds[1].Id, null, AdmissionType.Planned, null, "x", null, null, "test").Ok); // already in
        }

        [Fact]
        public void Database_refuses_a_second_active_stay_in_the_same_bed()
        {
            // Two people admitting at the same moment both pass the service check; the filtered unique index catches it.
            var (scope, db, _) = Open();
            using var __ = scope;
            var ward = TestData.Ward(db, 1);
            var bed = ward.Beds[0].Id;
            db.Admissions.Add(new Admission { PatientId = TestData.Patient(db).Id, BedId = bed, AdmittedAt = ClinicTime.Now });
            db.SaveChanges();
            db.Admissions.Add(new Admission { PatientId = TestData.Patient(db).Id, BedId = bed, AdmittedAt = ClinicTime.Now });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        [Fact]
        public void Moving_closes_the_old_stay_and_frees_the_bed()
        {
            var (scope, db, svc) = Open();
            using var scopeLifetime = scope;
            var cheap = TestData.Ward(db, 1, price: 40);
            var vip = TestData.Ward(db, 1, price: 90);
            var p = TestData.Patient(db);
            var a = svc.Admit(p.Id, cheap.Beds[0].Id, null, AdmissionType.Emergency, null, "x", null, null, "test").Admission!;

            Assert.True(svc.Move(a, vip.Beds[0].Id, "test").Ok);

            var stays = db.BedStays.AsNoTracking().Where(s => s.AdmissionId == a.Id).OrderBy(s => s.Id).ToList();
            Assert.Equal(2, stays.Count);
            Assert.NotNull(stays[0].To);
            Assert.Null(stays[1].To);
            Assert.Equal(90, stays[1].DailyPrice);
            Assert.Contains(svc.FreeBeds(), b => b.Id == cheap.Beds[0].Id);
        }

        [Fact]
        public void Dose_is_recorded_once_and_a_given_stock_dose_leaves_the_ward_store()
        {
            var (scope, db, svc) = Open();
            using var scopeLifetime = scope;
            var stock = scope.ServiceProvider.GetRequiredService<StockService>();
            var dept = TestData.Department(db);
            var store = TestData.Warehouse(db, dept.Id);
            var drug = TestData.Item(db, StockItemKind.Medicine, "ampul");
            stock.Receive(store.Id, null, null, ClinicTime.Today, null, new[] { new StockService.ReceiptLine(drug.Id, 5, 1, "D", ClinicTime.Today.AddYears(1)) }, "test");
            var ward = TestData.Ward(db, 1, departmentId: dept.Id);
            var a = svc.Admit(TestData.Patient(db).Id, ward.Beds[0].Id, null, AdmissionType.Planned, null, "x", null, null, "test").Admission!;

            var now = ClinicTime.Now;
            var slot = now.Date.AddHours(now.Hour);
            var order = new MedicationOrder
            {
                AdmissionId = a.Id, StockItemId = drug.Id, DrugName = drug.Name, QuantityPerDose = 2,
                Times = slot.ToString("HH:mm"), StartAt = now.AddHours(-3), OrderedAt = now.AddHours(-3)
            };
            db.MedicationOrders.Add(order);
            db.SaveChanges();
            order = db.MedicationOrders.Include(o => o.Admission).Include(o => o.StockItem).First(o => o.Id == order.Id);

            Assert.False(svc.RecordDose(order, slot, DoseStatus.Skipped, null, "nurse", out _).Ok);          // reason required
            Assert.False(svc.RecordDose(order, slot.AddMinutes(7), DoseStatus.Given, null, "nurse", out _).Ok); // not a scheduled time
            var given = svc.RecordDose(order, slot, DoseStatus.Given, null, "nurse", out var warning);
            Assert.True(given.Ok, given.Error);
            Assert.Null(warning);
            Assert.False(svc.RecordDose(order, slot, DoseStatus.Given, null, "nurse", out _).Ok);           // twice

            Assert.Equal(3, db.StockBatches.AsNoTracking().Where(b => b.WarehouseId == store.Id).Sum(b => b.Quantity));
            var dose = db.MedicationDoses.AsNoTracking().Single(d => d.MedicationOrderId == order.Id);
            var doc = db.StockDocuments.AsNoTracking().Single(d => d.Id == dose.StockDocumentId);
            Assert.Equal(a.Id, doc.AdmissionId);
            Assert.Equal(a.PatientId, doc.PatientId);
        }

        [Fact]
        public void Discharge_frees_the_bed_and_stops_orders_and_records_block_cancel()
        {
            var (scope, db, svc) = Open();
            using var scopeLifetime = scope;
            var ward = TestData.Ward(db, 1);
            var a = svc.Admit(TestData.Patient(db).Id, ward.Beds[0].Id, null, AdmissionType.Planned, null, "x", null, null, "test").Admission!;
            db.MedicationOrders.Add(new MedicationOrder { AdmissionId = a.Id, DrugName = "Parasetamol", StartAt = ClinicTime.Now, OrderedAt = ClinicTime.Now });
            db.SaveChanges();

            Assert.False(svc.Cancel(Load(db, a.Id), "səhv", "test").Ok); // has an order

            var r = svc.Discharge(Load(db, a.Id), DischargeOutcome.Recovered, "J18.9", "Pnevmoniya", null, null, null, "doctor");
            Assert.True(r.Ok, r.Error);

            var after = db.Admissions.AsNoTracking().Single(x => x.Id == a.Id);
            Assert.Equal(AdmissionStatus.Discharged, after.Status);
            Assert.Null(after.BedId);
            Assert.All(db.MedicationOrders.AsNoTracking().Where(o => o.AdmissionId == a.Id), o => Assert.NotNull(o.StoppedAt));
            Assert.Contains(svc.FreeBeds(), b => b.Id == ward.Beds[0].Id);
            Assert.False(svc.Discharge(Load(db, a.Id), DischargeOutcome.Recovered, null, "x", null, null, null, "doctor").Ok);
        }

        [Fact]
        public void Mistaken_admission_without_records_can_be_cancelled()
        {
            var (scope, db, svc) = Open();
            using var scopeLifetime = scope;
            var ward = TestData.Ward(db, 1);
            var a = svc.Admit(TestData.Patient(db).Id, ward.Beds[0].Id, null, AdmissionType.Planned, null, "x", null, null, "test").Admission!;

            Assert.False(svc.Cancel(Load(db, a.Id), " ", "test").Ok); // reason required
            Assert.True(svc.Cancel(Load(db, a.Id), "Səhv xəstə", "test").Ok);
            Assert.Equal(AdmissionStatus.Cancelled, db.Admissions.AsNoTracking().Single(x => x.Id == a.Id).Status);
        }

        [Fact]
        public void Man_in_a_womens_ward_gets_a_warning()
        {
            var bed = new Bed { Name = "1", Ward = new Ward { Name = "Qadın palatası", Gender = WardGender.Female } };
            Assert.NotNull(InpatientRules.GenderWarning(bed, new Patient { Gender = Gender.Male }));
            Assert.Null(InpatientRules.GenderWarning(bed, new Patient { Gender = Gender.Female }));
            Assert.Null(InpatientRules.GenderWarning(bed, new Patient()));
        }
    }
}
