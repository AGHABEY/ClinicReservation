using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec C1–C4: queue priority, reception / cashier windows, the kiosk.</summary>
    [Collection(DbCollection.Name)]
    public class QueueDeskKioskHttpTests
    {
        private readonly AppFixture _app;
        public QueueDeskKioskHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private int Visit(int doctorId, Patient p, DateTime at, string status = StatusNames.Confirmed) => Db(db =>
        {
            var r = TestData.Reserve(db, db.Patients.Find(p.Id)!, status, TestData.Exam(db));
            r.DoctorId = doctorId;
            r.AppointmentAt = at;
            r.PatientToken = Guid.NewGuid().ToString("N");
            db.SaveChanges();
            return r.Id;
        });

        private (string? Ticket, QueuePriority Priority, DateTime? CalledAt, string Status) Row(int id) =>
            Db(db => db.Reserves.Include(r => r.Status).Where(r => r.Id == id).Select(r => new { r.QueueTicket, r.Priority, r.CalledAt, r.Status!.Name }).ToList()
                .Select(x => (x.QueueTicket, x.Priority, x.CalledAt, x.Name)).Single());

        [Fact]
        public async Task Priority_patients_are_called_first_and_65_plus_is_set_by_itself()
        {
            var now = ClinicTime.Now;
            var (doctorId, young, old, other) = Db(db =>
            {
                var d = TestData.Doctor(db);
                var young = TestData.Patient(db);
                var old = TestData.Patient(db);
                old.Birthday = now.Date.AddYears(-70);
                var other = TestData.Patient(db);
                db.SaveChanges();
                return (d.Id, young, old, other);
            });
            var a = Visit(doctorId, young, now.AddMinutes(10));
            var b = Visit(doctorId, old, now.AddMinutes(20));
            var c = Visit(doctorId, other, now.AddMinutes(30));

            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            foreach (var id in new[] { a, b, c })
                await s.PostAsync($"/Reservation/Details/{id}", "/Reservation/Transition", ("id", id.ToString()), ("to", StatusNames.Arrived), ("returnTo", "details"));
            Assert.Equal(QueuePriority.None, Row(a).Priority);
            Assert.Equal(QueuePriority.Elderly, Row(b).Priority); // 70 years old

            // The last one is urgent: reception marks it.
            await s.PostAsync($"/Reservation/Details/{c}", $"/Reservation/Priority/{c}", ("priority", "Urgent"));
            Assert.Equal(QueuePriority.Urgent, Row(c).Priority);

            var screen = await s.Client.GetStringAsync("/Queue/Data");
            Assert.Contains("Təcili", System.Text.RegularExpressions.Regex.Unescape(screen));

            // Calls: urgent, then 65+, then the one who came first.
            var admin = await WebSession.LoginAsync(_app, AppRoles.Admin);
            var order = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                await admin.PostAsync($"/Workspace?doctorId={doctorId}", "/Workspace/CallNext", ("doctorId", doctorId.ToString()));
                order.Add(new[] { a, b, c }.Where(x => Row(x).CalledAt != null && !order.Contains(x)).Single());
            }
            Assert.Equal(new[] { c, b, a }, order);
        }

        [Fact]
        public async Task Reception_and_cashier_queues_call_tickets_to_windows()
        {
            var admin = await WebSession.LoginAsync(_app, AppRoles.Admin);
            var name = TestData.Unique("Pəncərə ");
            await admin.PostAsync("/Department", "/Department/SaveWindow", ("id", "0"), ("name", name), ("kind", "Reception"));
            var windowId = Db(db => db.ServiceWindows.Single(w => w.Name == name).Id);

            var kiosk = await WebSession.LoginAsync(_app, AppRoles.Kiosk);
            async Task<DeskTicket> TakeAsync(string kind)
            {
                var r = await kiosk.PostAsync("/Kiosk", "/Kiosk/Desk", ("kind", kind));
                Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
                var id = int.Parse(r.Headers.Location!.ToString().Split('?')[0].Split('/').Last());
                return Db(db => db.DeskTickets.AsNoTracking().Single(t => t.Id == id));
            }
            var first = await TakeAsync("Reception");
            var second = await TakeAsync("Reception");
            var cash = await TakeAsync("Cashier");
            Assert.StartsWith("R-", first.Ticket);
            Assert.Equal(first.Number + 1, second.Number);
            Assert.StartsWith("KS-", cash.Ticket);
            Assert.Contains(first.Ticket, await WebSession.TextAsync(await kiosk.Client.GetAsync($"/Kiosk/DeskTicket/{first.Id}?kiosk=true")));

            // Staff at the door prints an urgent ticket: it goes first.
            var reception = await WebSession.LoginAsync(_app, AppRoles.Reception);
            await reception.PostAsync($"/Desk?windowId={windowId}", "/Desk/Issue", ("kind", "Reception"), ("priority", "Urgent"), ("windowId", windowId.ToString()));
            var urgent = Db(db => db.DeskTickets.Where(t => t.Kind == DeskKind.Reception && t.Priority == QueuePriority.Urgent).OrderByDescending(t => t.Id).First());

            async Task<string> NextAsync() => await reception.FollowAsync(await reception.PostAsync($"/Desk?windowId={windowId}", "/Desk/Next", ("windowId", windowId.ToString())));
            Assert.Contains($"{urgent.Ticket} → {name}", await NextAsync());
            Assert.Contains(urgent.Ticket, System.Text.RegularExpressions.Regex.Unescape(await reception.Client.GetStringAsync("/Queue/Data")));

            // «Növbəti» serves the current one and calls the next by number; the cashier ticket isn't touched.
            Assert.Contains($"{first.Ticket} → {name}", await NextAsync());
            Assert.Equal(DeskTicketStatus.Served, Db(db => db.DeskTickets.Single(t => t.Id == urgent.Id).Status));
            await reception.PostAsync($"/Desk?windowId={windowId}", "/Desk/Finish", ("id", first.Id.ToString()), ("windowId", windowId.ToString()), ("missed", "true"));
            Assert.Equal(DeskTicketStatus.Missed, Db(db => db.DeskTickets.Single(t => t.Id == first.Id).Status));
            Assert.Equal(DeskTicketStatus.Waiting, Db(db => db.DeskTickets.Single(t => t.Id == cash.Id).Status));
        }

        [Fact]
        public async Task Kiosk_checks_in_by_FIN_card_or_QR_only_the_patients_own_visit_in_its_time_window()
        {
            var now = ClinicTime.Now;
            var (doctorId, p, stranger) = Db(db => (TestData.Doctor(db).Id, TestData.Patient(db), TestData.Patient(db)));
            var soon = Visit(doctorId, p, now.AddMinutes(20));
            var later = Visit(doctorId, p, now.AddHours(5) > now.Date.AddDays(1) ? now.AddMinutes(25) : now.AddHours(5));
            var theirs = Visit(doctorId, stranger, now.AddMinutes(30));

            var kiosk = await WebSession.LoginAsync(_app, AppRoles.Kiosk);
            // The kiosk login sees nothing else.
            var denied = await kiosk.Client.GetAsync("/Reservation");
            Assert.Contains("AccessDenied", denied.Headers.Location!.ToString());

            var found = await WebSession.TextAsync(await kiosk.PostAsync("/Kiosk", "/Kiosk/Find", ("code", p.PinCode.ToLower())));
            Assert.Contains($"{p.Name} {p.Surname[0]}.", found);
            Assert.DoesNotContain(p.Surname, found.Replace($"{p.Surname[0]}.", ""));
            if (Row(later).Ticket == null && later != soon && now.AddHours(5).Date == now.Date)
                Assert.Contains("dəq əvvəl açılır", found);

            // Someone else's visit can't be checked in with this FİN.
            await kiosk.PostAsync("/Kiosk", "/Kiosk/CheckIn", ("id", theirs.ToString()), ("code", p.PinCode));
            Assert.Null(Row(theirs).Ticket);

            var checkIn = await kiosk.PostAsync("/Kiosk", "/Kiosk/CheckIn", ("id", soon.ToString()), ("code", p.PinCode));
            Assert.Equal(HttpStatusCode.Redirect, checkIn.StatusCode);
            var row = Row(soon);
            Assert.Equal(StatusNames.Arrived, row.Status);
            Assert.NotNull(row.Ticket);
            var ticket = await WebSession.TextAsync(await kiosk.Client.GetAsync(checkIn.Headers.Location!.ToString()));
            Assert.Contains(row.Ticket!, ticket);
            Assert.DoesNotContain("/Reservation/Details", ticket);
            Assert.True(Db(db => db.AuditLogs.Any(a => a.EntityId == soon.ToString() && a.UserName == "Kiosk")));

            // The stranger checks in with the QR of their SMS link.
            var token = Db(db => db.Reserves.Single(r => r.Id == theirs).PatientToken!);
            await kiosk.PostAsync("/Kiosk", "/Kiosk/CheckIn", ("id", theirs.ToString()), ("code", $"https://clinic.az/r/{token}"));
            Assert.Equal(StatusNames.Arrived, Row(theirs).Status);

            // A loyalty card finds its members' visits.
            var number = Db(db =>
            {
                var card = new LoyaltyCard { Number = "MC" + Random.Shared.Next(10_000_000, 99_999_999), HolderPatientId = p.Id, CreatedAt = ClinicTime.Now };
                card.Members.Add(new LoyaltyCardMember { PatientId = p.Id, JoinedAt = ClinicTime.Now });
                db.LoyaltyCards.Add(card);
                db.SaveChanges();
                return card.Number;
            });
            Assert.Contains("Artıq qeydiyyatdan keçmisiniz", await WebSession.TextAsync(await kiosk.PostAsync("/Kiosk", "/Kiosk/Find", ("code", number))));
        }
    }
}
