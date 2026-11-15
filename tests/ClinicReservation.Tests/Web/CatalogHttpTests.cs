using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Patient cards, doctors with schedules and leaves, departments, examinations.</summary>
    [Collection(DbCollection.Name)]
    public class CatalogHttpTests
    {
        private readonly AppFixture _app;
        public CatalogHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task Patient_card_rules_and_lookup()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var existing = Db(db => TestData.Patient(db));

            var dup = await s.PostAsync("/Patient/Create", "/Patient/Save", ("Id", "0"), ("Name", "X"), ("Surname", "Y"), ("PinCode", existing.PinCode.ToLowerInvariant()),
                ("Phone", "050 111 22 33"), ("Birthday", ClinicTime.Today.AddDays(1).ToString("yyyy-MM-dd")));
            var text = await WebSession.TextAsync(dup);
            Assert.Contains("Bu FİN kodla xəstə artıq mövcuddur", text);
            Assert.Contains("Doğum tarixi gələcəkdə ola bilməz", text);

            var lookup = await s.Client.GetStringAsync($"/Patient/Lookup?pin={existing.PinCode.ToLowerInvariant()}");
            Assert.Contains("\"found\":true", lookup);
            Assert.Contains("\"found\":false", await s.Client.GetStringAsync("/Patient/Lookup?pin=NOPE000"));
        }

        [Fact]
        public async Task Doctor_schedule_validation_and_leave_warning()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Admin);
            var name = TestData.Unique("Dr ");
            (string, string)[] Days(string start, string end, string? bs = null, string? be = null) =>
                Enumerable.Range(0, 7).SelectMany(i => new[]
                {
                    ($"Days[{i}].Day", ((int)new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }[i]).ToString()),
                    ($"Days[{i}].Works", i < 5 ? "true" : "false"),
                    ($"Days[{i}].Start", start), ($"Days[{i}].End", end),
                    ($"Days[{i}].BreakStart", bs ?? ""), ($"Days[{i}].BreakEnd", be ?? "")
                }).ToArray();

            var bad = await s.PostAsync("/Doctor/Create", "/Doctor/Save",
                new[] { ("Id", "0"), ("FullName", name), ("SlotMinutes", "30"), ("IsActive", "true") }.Concat(Days("09:00", "18:00", "19:00", "20:00")).ToArray());
            Assert.Contains("fasilə iş saatının içində olmalıdır", await WebSession.TextAsync(bad));

            var ok = await s.PostAsync("/Doctor/Create", "/Doctor/Save",
                new[] { ("Id", "0"), ("FullName", name), ("SlotMinutes", "20"), ("IsActive", "true") }.Concat(Days("09:00", "17:00", "13:00", "14:00")).ToArray());
            Assert.Contains("əlavə edildi", await s.FollowAsync(ok));
            var doctor = Db(db => db.Doctors.Include(d => d.WorkHours).Single(d => d.FullName == name));
            Assert.Equal(5, doctor.WorkHours.Count);

            // A leave over an active booking is saved, with a warning.
            Db(db =>
            {
                var r = TestData.Reserve(db, TestData.Patient(db), StatusNames.Confirmed, TestData.Exam(db));
                r.DoctorId = doctor.Id;
                r.AppointmentAt = ClinicTime.Today.AddDays(3).AddHours(10);
                return db.SaveChanges();
            });
            var leave = await s.PostAsync($"/Doctor/Edit/{doctor.Id}", "/Doctor/AddLeave", ("DoctorId", doctor.Id.ToString()),
                ("From", ClinicTime.Today.AddDays(2).ToString("yyyy-MM-dd")), ("To", ClinicTime.Today.AddDays(4).ToString("yyyy-MM-dd")), ("Reason", "Məzuniyyət"));
            Assert.Contains("1 aktiv rezervasiyası var", await s.FollowAsync(leave));
            Assert.True(Db(db => db.DoctorLeaves.Any(l => l.DoctorId == doctor.Id)));
        }

        [Fact]
        public async Task Department_prefix_and_examination_with_materials()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Admin);
            var badPrefix = await s.PostAsync("/Department", "/Department/SaveDepartment", ("id", "0"), ("name", TestData.Unique("Ş")), ("queuePrefix", "K1"));
            Assert.Contains("Talon hərfi 1–3 latın hərfi", await s.FollowAsync(badPrefix));

            var item = Db(db => TestData.Item(db));
            var name = TestData.Unique("Müayinə ");
            var zero = await s.PostAsync("/ExaminationType/Add", "/ExaminationType/Add", ("Name", name), ("Price", "30"),
                ("materialRows[0].StockItemId", item.Id.ToString()), ("materialRows[0].Quantity", "0"));
            Assert.Contains("Material sətirlərində miqdar 0-dan böyük olmalıdır", await WebSession.TextAsync(zero));

            var ok = await s.PostAsync("/ExaminationType/Add", "/ExaminationType/Add", ("Name", name), ("Price", "30"),
                ("materialRows[0].StockItemId", item.Id.ToString()), ("materialRows[0].Quantity", "2"),
                ("materialRows[1].StockItemId", item.Id.ToString()), ("materialRows[1].Quantity", "1"));
            Assert.Contains("əlavə edildi", await s.FollowAsync(ok));
            var exam = Db(db => db.ExaminationTypes.Include(e => e.Materials).Single(e => e.Name == name));
            Assert.Equal(3, Assert.Single(exam.Materials).Quantity); // duplicates added up
        }
    }
}
