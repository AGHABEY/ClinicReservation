using System.Net;
using System.Text.Json;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec A5 (calendar, drag to move) and A6 (extra visits: permission + the doctor's daily limit).</summary>
    [Collection(DbCollection.Name)]
    public class CalendarHttpTests
    {
        private readonly AppFixture _app;
        public CalendarHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");

        private int Visit(int doctorId, DateTime at, string status = StatusNames.Confirmed, int? roomId = null) => Db(db =>
        {
            var r = TestData.Reserve(db, TestData.Patient(db), status, TestData.Exam(db));
            r.DoctorId = doctorId;
            r.AppointmentAt = at;
            r.DurationMinutes = 30;
            r.RoomId = roomId;
            db.SaveChanges();
            return r.Id;
        });

        private static async Task<(bool Ok, string Message)> MoveAsync(WebSession s, int id, int doctorId, DateTime date, string time)
        {
            var r = await s.PostAsync($"/Calendar?mode=day&date={D(date)}", "/Calendar/Move",
                ("id", id.ToString()), ("doctorId", doctorId.ToString()), ("date", D(date)), ("time", time));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            return (json.RootElement.GetProperty("ok").GetBoolean(), json.RootElement.GetProperty("message").GetString()!);
        }

        [Fact]
        public async Task Day_and_week_views_show_doctors_hours_leave_and_visits()
        {
            var day = ClinicTime.Today.AddDays(3);
            var (deptId, a, b) = Db(db =>
            {
                var dept = TestData.Department(db);
                var a = TestData.Doctor(db, dept.Id);
                var b = TestData.Doctor(db, dept.Id);
                db.DoctorLeaves.Add(new DoctorLeave { DoctorId = b.Id, From = day, To = day, Reason = "Konfrans" });
                db.SaveChanges();
                return (dept.Id, a, b);
            });
            var id = Visit(a.Id, day.AddHours(11));
            var patient = Db(db => db.Reserves.Include(r => r.Patient).Single(r => r.Id == id).Patient.FullName);

            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var dayView = await WebSession.TextAsync(await s.Client.GetAsync($"/Calendar?mode=day&date={D(day)}&departmentId={deptId}"));
            Assert.Contains(a.FullName, dayView);
            Assert.Contains(b.FullName, dayView);
            Assert.Contains("Məzuniyyət: Konfrans", dayView);
            Assert.Contains(patient, dayView);
            Assert.Contains($"data-id=\"{id}\"", dayView);
            Assert.Contains("draggable=\"true\"", dayView);

            var weekView = await WebSession.TextAsync(await s.Client.GetAsync($"/Calendar?mode=week&date={D(day)}&doctorId={a.Id}"));
            Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(weekView, "class=\"cal-col\"").Count);
            Assert.Contains(patient, weekView);

            // A doctor (view-only for bookings) sees the calendar but can't drag.
            var doc = await WebSession.LoginAsync(_app, AppRoles.Doctor);
            var readOnly = await WebSession.TextAsync(await doc.Client.GetAsync($"/Calendar?mode=day&date={D(day)}&departmentId={deptId}"));
            Assert.Contains(patient, readOnly);
            Assert.DoesNotContain("draggable=\"true\"", readOnly);
        }

        [Fact]
        public async Task Dragging_moves_a_visit_only_to_a_free_future_time()
        {
            var day = ClinicTime.Today.AddDays(4);
            var (a, b, roomB) = Db(db =>
            {
                var room = new Room { Name = TestData.Unique("Kab ") };
                db.Rooms.Add(room);
                db.SaveChanges();
                var a = TestData.Doctor(db);
                var b = TestData.Doctor(db);
                b.RoomId = room.Id;
                db.SaveChanges();
                return (a.Id, b.Id, room.Id);
            });
            var id = Visit(a, day.AddHours(10));
            Visit(a, day.AddHours(12));
            var past = Visit(a, ClinicTime.Today.AddDays(-1).AddHours(10));
            var completed = Visit(a, day.AddHours(15), StatusNames.Completed);
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);

            var busy = await MoveAsync(s, id, a, day, "12:00");
            Assert.False(busy.Ok);
            Assert.Contains("doludur", busy.Message);
            Assert.False((await MoveAsync(s, past, a, day, "16:00")).Ok);
            Assert.False((await MoveAsync(s, completed, a, day, "16:00")).Ok);
            Assert.False((await MoveAsync(s, id, a, ClinicTime.Today.AddDays(-2), "10:00")).Ok);

            var moved = await MoveAsync(s, id, a, day.AddDays(1), "09:30");
            Assert.True(moved.Ok, moved.Message);
            Assert.Equal(day.AddDays(1).AddHours(9.5), Db(db => db.Reserves.Single(r => r.Id == id).AppointmentAt));

            // To another doctor: the visit takes that doctor's room.
            var other = await MoveAsync(s, id, b, day, "14:00");
            Assert.True(other.Ok, other.Message);
            var row = Db(db => db.Reserves.AsNoTracking().Single(r => r.Id == id));
            Assert.Equal((b, roomB, day.AddHours(14)), (row.DoctorId!.Value, row.RoomId!.Value, row.AppointmentAt));
            Assert.True(Db(db => db.AuditLogs.Count(x => x.EntityId == id.ToString() && x.Action == "Rezervasiya köçürüldü") == 2));
        }

        [Fact]
        public async Task Extra_visit_needs_the_permission_and_stays_within_the_doctors_daily_limit()
        {
            var day = ClinicTime.Today.AddDays(5);
            var (doctorId, examId) = Db(db =>
            {
                var d = TestData.Doctor(db);
                d.MaxOverbookPerDay = 1;
                db.SaveChanges();
                return (d.Id, TestData.Exam(db).Id);
            });
            Visit(doctorId, day.AddHours(10));
            Visit(doctorId, day.AddHours(11));

            (string, string)[] Form(string time, bool overbook) =>
            new[]
            {
                ("PinCode", Guid.NewGuid().ToString("N")[..7].ToUpperInvariant()), ("Name", "Əlavə"), ("Surname", "Xəstə"),
                ("Phone", "050 111 22 33"), ("Birthday", "1990-01-01"), ("DoctorId", doctorId.ToString()),
                ("AppointmentDate", D(day)), ("AppointmentTime", time), ("ExaminationTypeIds", examId.ToString()),
                ("Overbook", overbook ? "true" : "false")
            };

            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var form = await WebSession.TextAsync(await s.Client.GetAsync($"/Reservation/Add?doctorId={doctorId}&date={D(day)}"));
            Assert.Contains("id=\"overbook\"", form);

            // Busy without «araya sal» → refused.
            Assert.Contains("Bu saat artıq doludur", await WebSession.TextAsync(await s.PostAsync("/Reservation/Add", "/Reservation/Add", Form("10:00", false))));

            // With it → booked as an extra visit.
            var ok = await s.PostAsync("/Reservation/Add", "/Reservation/Add", Form("10:00", true));
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            var details = await s.FollowAsync(ok);
            Assert.Contains("Əlavə qəbul", details);
            Assert.Equal(1, Db(db => db.Reserves.Count(r => r.DoctorId == doctorId && r.Overbooked)));

            // The doctor's limit is 1 a day.
            Assert.Contains("əlavə qəbul limiti dolub (1/1)", await WebSession.TextAsync(await s.PostAsync("/Reservation/Add", "/Reservation/Add", Form("11:00", true))));

            // A free time with the box ticked is just a normal visit.
            var free = await s.PostAsync("/Reservation/Add", "/Reservation/Add", Form("13:00", true));
            Assert.Equal(HttpStatusCode.Redirect, free.StatusCode);
            Assert.Equal(1, Db(db => db.Reserves.Count(r => r.DoctorId == doctorId && r.Overbooked)));

            // Staff can book but not squeeze in.
            var staff = await WebSession.LoginAsync(_app, AppRoles.Staff);
            Assert.DoesNotContain("id=\"overbook\"", await WebSession.TextAsync(await staff.Client.GetAsync($"/Reservation/Add?doctorId={doctorId}&date={D(day)}")));
            Assert.Contains("icazəniz yoxdur", await WebSession.TextAsync(await staff.PostAsync("/Reservation/Add", "/Reservation/Add", Form("11:00", true))));

            var cal = await WebSession.TextAsync(await s.Client.GetAsync($"/Calendar?mode=day&date={D(day)}&doctorId={doctorId}"));
            Assert.Contains("class=\"tag\">Əlavə", cal);
        }
    }
}
