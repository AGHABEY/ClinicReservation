using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Lab work through the forms: sample, results, validation rules, reopen, catalog.</summary>
    [Collection(DbCollection.Name)]
    public class LabHttpTests
    {
        private readonly AppFixture _app;
        public LabHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Sample_results_validation_and_reopen()
        {
            var lab = await WebSession.LoginAsync(_app, AppRoles.Lab);
            var (reserveId, hgbCode, urineCode) = Db(db =>
            {
                var hgb = new LabTest { Code = TestData.Unique("H"), Name = "Hemoqlobin", SampleType = "Qan", ResultType = LabResultType.Numeric, RefMin = 120, RefMax = 160, CriticalMin = 70, Unit = "q/L" };
                var pro = new LabTest { Code = TestData.Unique("P"), Name = "Zülal", SampleType = "Sidik", ResultType = LabResultType.Choice, Choices = "Mənfi;Müsbət", RefText = "Mənfi" };
                var exam = TestData.Exam(db);
                exam.LabTests.Add(new ExaminationTypeLabTest { LabTest = hgb });
                exam.LabTests.Add(new ExaminationTypeLabTest { LabTest = pro });
                db.SaveChanges();
                var r = TestData.Reserve(db, TestData.Patient(db, Gender.Female), StatusNames.Arrived, exam);
                return (r.Id, hgb.Code, pro.Code);
            });

            // Taking the sample makes one tube per sample type.
            var collect = await lab.PostAsync("/Lab", "/Lab/Collect", ("reserveId", reserveId.ToString()));
            Assert.Contains("Nümunə götürüldü", await lab.FollowAsync(collect));
            var orders = Db(db => db.LabOrders.Include(o => o.Results).ThenInclude(r => r.LabTest).Where(o => o.ReserveId == reserveId).AsNoTracking().ToList());
            Assert.Equal(2, orders.Count);
            var blood = orders.Single(o => o.Results.Any(r => r.LabTest.Code == hgbCode));
            var urine = orders.Single(o => o.Results.Any(r => r.LabTest.Code == urineCode));
            var hgbId = blood.Results.Single().Id;
            var proId = urine.Results.Single().Id;

            // A scanned barcode opens the order.
            var scan = await lab.Client.GetAsync($"/Lab?q={blood.Barcode}");
            Assert.EndsWith($"/Lab/Order/{blood.Id}", scan.Headers.Location!.ToString());

            // Wrong inputs: nothing saved, typed values stay.
            var notNumber = await lab.PostAsync($"/Lab/Order/{blood.Id}", "/Lab/Save", ("OrderId", blood.Id.ToString()), ("Results[0].Id", hgbId.ToString()), ("Results[0].Value", "abc"));
            Assert.Contains("Hemoqlobin: rəqəm daxil edin", await WebSession.TextAsync(notNumber));
            var wrongChoice = await lab.PostAsync($"/Lab/Order/{urine.Id}", "/Lab/Save", ("OrderId", urine.Id.ToString()), ("Results[0].Id", proId.ToString()), ("Results[0].Value", "Yaşıl"));
            Assert.Contains("Zülal: siyahıdan seçin", await WebSession.TextAsync(wrongChoice));
            Assert.Null(Db(db => db.LabResults.Single(r => r.Id == proId).Value));

            // A critical value needs the confirmation before validation.
            var critical = await lab.PostAsync($"/Lab/Order/{blood.Id}", "/Lab/Save", ("OrderId", blood.Id.ToString()), ("Results[0].Id", hgbId.ToString()), ("Results[0].Value", "62"), ("Validate", "1"));
            Assert.Contains("Kritik dəyər var", await WebSession.TextAsync(critical));
            var validated = await lab.PostAsync($"/Lab/Order/{blood.Id}", "/Lab/Save", ("OrderId", blood.Id.ToString()), ("Results[0].Id", hgbId.ToString()), ("Results[0].Value", "62"),
                ("Validate", "1"), ("ConfirmCritical", "true"), ("SendSms", "true"));
            Assert.Contains("bütün nümunələri təsdiqlənəndə göndəriləcək", await lab.FollowAsync(validated)); // urine still open
            var saved = Db(db => db.LabResults.Single(r => r.Id == hgbId));
            Assert.Equal(LabFlag.CriticalLow, saved.Flag);
            Assert.Equal(62, saved.NumericValue);

            var last = await lab.PostAsync($"/Lab/Order/{urine.Id}", "/Lab/Save", ("OrderId", urine.Id.ToString()), ("Results[0].Id", proId.ToString()), ("Results[0].Value", "Müsbət"),
                ("Validate", "1"), ("SendSms", "true"));
            Assert.Contains("SMS", await lab.FollowAsync(last));
            Assert.True(Db(db => db.SmsMessages.Any(m => m.ReserveId == reserveId && m.Kind == ClinicReservation.Services.SmsService.KindLabResult)));
            Assert.Equal(LabFlag.Abnormal, Db(db => db.LabResults.Single(r => r.Id == proId).Flag));

            // Validated results are locked; reopening needs a reason.
            var noReason = await lab.PostAsync($"/Lab/Order/{blood.Id}", "/Lab/Reopen", ("id", blood.Id.ToString()), ("reason", " "));
            Assert.Contains("Düzəliş üçün səbəb yazın", await lab.FollowAsync(noReason));
            var reopened = await lab.PostAsync($"/Lab/Order/{blood.Id}", "/Lab/Reopen", ("id", blood.Id.ToString()), ("reason", "Təkrar ölçmə"));
            Assert.Contains("düzəliş üçün açıldı", await lab.FollowAsync(reopened));
            Assert.Equal(LabOrderStatus.Collected, Db(db => db.LabOrders.Single(o => o.Id == blood.Id).Status));
        }

        [Fact]
        public async Task Catalog_form_validates_ranges_and_choices()
        {
            var admin = await WebSession.LoginAsync(_app, AppRoles.Admin);
            var code = TestData.Unique("K");
            var badRange = await admin.PostAsync("/LabTest/Create", "/LabTest/Save", ("Id", "0"), ("Code", code), ("Name", "Test"), ("SampleType", "Qan"),
                ("ResultType", "Numeric"), ("RefMin", "10"), ("RefMax", "5"), ("SortOrder", "1"), ("IsActive", "true"));
            Assert.Contains("Yuxarı hədd aşağı həddən kiçikdir", await WebSession.TextAsync(badRange));

            var badChoice = await admin.PostAsync("/LabTest/Create", "/LabTest/Save", ("Id", "0"), ("Code", code), ("Name", "Test"), ("SampleType", "Sidik"),
                ("ResultType", "Choice"), ("Choices", "Mənfi;Müsbət"), ("RefText", "Normal"), ("SortOrder", "1"), ("IsActive", "true"));
            Assert.Contains("Normal dəyər variantlardan biri olmalıdır", await WebSession.TextAsync(badChoice));

            var ok = await admin.PostAsync("/LabTest/Create", "/LabTest/Save", ("Id", "0"), ("Code", code.ToLowerInvariant()), ("Name", "Test"), ("SampleType", "Qan"),
                ("ResultType", "Numeric"), ("RefMin", "1.5"), ("RefMax", "3"), ("Unit", "mmol/L"), ("SortOrder", "1"), ("IsActive", "true"));
            Assert.Contains("«Test» əlavə edildi", await admin.FollowAsync(ok));
            Assert.Equal(1.5m, Db(db => db.LabTests.Single(t => t.Code == code.ToUpperInvariant()).RefMin));
        }
    }
}
