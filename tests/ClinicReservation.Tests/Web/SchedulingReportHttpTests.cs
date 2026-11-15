using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec F1: the visits report adds up — load, no-shows, cancellations, lead time, new/returning, sources, discounts, waiting, ratings.</summary>
    [Collection(DbCollection.Name)]
    public class SchedulingReportHttpTests
    {
        private readonly AppFixture _app;
        public SchedulingReportHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task The_visits_report_counts_what_happened_in_the_period()
        {
            var day1 = ClinicTime.Today.AddDays(-10);
            var day2 = day1.AddDays(1);
            var (deptId, doctorName) = Db(db =>
            {
                var dept = TestData.Department(db);
                var doc = TestData.Doctor(db, dept.Id);              // works 08–20 every day: 720 min/day
                var exam = TestData.Exam(db); exam.Price = 100;
                var returning = TestData.Patient(db);
                var older = TestData.Reserve(db, returning, StatusNames.Completed, exam);  // an earlier completed visit
                older.DoctorId = doc.Id;
                older.AppointmentAt = day1.AddDays(-30).AddHours(9);
                var reason = db.CancellationReasons.First(r => r.IsActive);
                var campaign = new Campaign { Name = TestData.Unique("Hesabat "), Type = DiscountType.Percent, Value = 10, StartsOn = day1, EndsOn = day2 };
                db.Campaigns.Add(campaign);
                db.SaveChanges();

                Reserve Visit(Patient p, string status, DateTime at, DateTime added, ReserveSource source)
                {
                    var r = TestData.Reserve(db, p, status, exam);
                    r.DoctorId = doc.Id; r.AppointmentAt = at; r.AddedDate = added; r.Source = source; r.DurationMinutes = 30;
                    return r;
                }
                // 1) returning patient, completed, waited 15 min, booked 5 days ahead, 10% campaign, rated 4.
                var a = Visit(returning, StatusNames.Completed, day1.AddHours(10), day1.AddDays(-5), ReserveSource.Phone);
                a.ArrivedAt = day1.AddHours(9).AddMinutes(55); a.StartedAt = day1.AddHours(10).AddMinutes(10); a.CompletedAt = day1.AddHours(10).AddMinutes(30);
                var line = a.ReserveExaminationTypes!.Single();
                line.ListPrice = 100; line.Discount = 10; line.Price = 90; line.DiscountKind = DiscountKind.Campaign; line.DiscountRefId = campaign.Id; line.DiscountSource = "Kampaniya";
                // 2) new patient, didn't come (automatic), booked the same day, online.
                var b = Visit(TestData.Patient(db), StatusNames.NoShow, day1.AddHours(11), day1, ReserveSource.Online);
                b.NoShowAuto = true;
                // 3) new patient, cancelled with a reason by the patient, booked 20 days ahead.
                var c = Visit(TestData.Patient(db), StatusNames.Cancelled, day2.AddHours(9), day2.AddDays(-20), ReserveSource.Reception);
                c.CancellationReasonId = reason.Id; c.CancelledBy = CancelledBy.Patient;
                db.SaveChanges();
                db.VisitRatings.Add(new VisitRating { ReserveId = a.Id, PatientId = returning.Id, DoctorId = doc.Id, Score = 4, CreatedAt = day1.AddHours(12) });
                db.SaveChanges();
                return (dept.Id, doc.FullName);
            });

            using (var scope = _app.Scope())
            {
                var v = await scope.ServiceProvider.GetRequiredService<SchedulingReportQueries>().LoadAsync(day1, day2, deptId);
                Assert.Equal((3, 1, 1, 1, 1, 2), (v.Booked, v.Completed, v.Cancelled, v.NoShows, v.NoShowsAuto, v.PastVisits));
                Assert.Equal(50, v.NoShowPercent);                         // 1 of 2 past (not cancelled) visits
                Assert.Equal((1, 1), (v.NewPatients, v.ReturningPatients));
                Assert.Equal(15, v.AvgWaitMinutes);
                Assert.Equal(4.0, v.Rating);
                Assert.Equal((100m * 2, 10m), (v.ListTotal, v.DiscountTotal)); // the cancelled visit isn't counted
                Assert.Equal(1, v.Campaigns.Single().Lines);
                Assert.Equal(new[] { 1, 0, 1, 0, 1 }, v.LeadTimes.Select(x => x.Count).ToArray()); // same day, 4–7, 15+
                Assert.Contains(v.Sources, s => s.Label == BookingLabels.Source(ReserveSource.Online) && s.Count == 1);
                var load = v.Doctors.Single();
                Assert.Equal((1440, 60, 30, 50), (load.WorkMinutes, load.BookedMinutes, load.AttendedMinutes, load.NoShowPercent));
                Assert.Equal((15, 10), (load.AvgWaitMinutes, load.AvgLateMinutes));
            }

            var manager = await WebSession.LoginAsync(_app, AppRoles.Manager);
            var range = $"from={day1:yyyy-MM-dd}&to={day2:yyyy-MM-dd}&departmentId={deptId}";
            var page = await WebSession.TextAsync(await manager.Client.GetAsync($"/Report/Visits?{range}"));
            Assert.Contains(doctorName, page);
            Assert.Contains("Ləğv səbəbləri", page);
            var csv = await manager.Client.GetAsync($"/Report/VisitsCsv?{range}");
            Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
            Assert.Contains($"{doctorName};1440;60;4;30;2;2;1;50;15;10;4.00", await csv.Content.ReadAsStringAsync());

            // Reception can't see reports.
            var reception = await WebSession.LoginAsync(_app, AppRoles.Reception);
            Assert.Contains("AccessDenied", (await reception.Client.GetAsync($"/Report/Visits?{range}")).Headers.Location!.ToString());
        }
    }
}
