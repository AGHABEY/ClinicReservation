using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Notifications;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec A3: the waitlist — a freed time is offered first-come, declined to the next, booked from the list.</summary>
    [Collection(DbCollection.Name)]
    public class WaitlistHttpTests
    {
        private readonly AppFixture _app;
        public WaitlistHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");

        /// <summary>A booked visit with this doctor at the given time.</summary>
        private int Booked(int doctorId, DateTime at, int? examId = null) => Db(db =>
        {
            var r = TestData.Reserve(db, TestData.Patient(db), StatusNames.Confirmed, examId == null ? TestData.Exam(db) : db.ExaminationTypes.Find(examId)!);
            r.DoctorId = doctorId;
            r.AppointmentAt = at;
            r.DurationMinutes = 30;
            r.PatientToken = Guid.NewGuid().ToString("N");
            db.SaveChanges();
            return r.Id;
        });

        private async Task<int> AddEntryAsync(WebSession s, Patient p, params (string, string)[] extra)
        {
            var fields = new List<(string, string)> { ("PinCode", p.PinCode), ("NotifyBySms", "true") };
            fields.AddRange(extra);
            var r = await s.PostAsync("/Waitlist/Add", "/Waitlist/Add", fields.ToArray());
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            return Db(db => db.WaitlistEntries.Where(w => w.PatientId == p.Id).OrderByDescending(w => w.Id).Select(w => w.Id).First());
        }

        private WaitlistEntry Entry(int id) => Db(db => db.WaitlistEntries.AsNoTracking().Single(w => w.Id == id));

        private Task<HttpResponseMessage> CancelAsync(WebSession s, int reserveId) =>
            s.PostAsync($"/Reservation/Details/{reserveId}", "/Reservation/Cancel", ("Id", reserveId.ToString()), ("ReasonId", Db(TestData.ReasonId).ToString()));

        [Fact]
        public async Task Freed_time_goes_to_the_first_waiting_patient_then_the_next_and_is_booked_from_the_list()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var (doctorId, examId, first, second) = Db(db => (TestData.Doctor(db).Id, TestData.Exam(db).Id, TestData.Patient(db), TestData.Patient(db)));
            var slot = ClinicTime.Today.AddDays(2).AddHours(10);
            var reserveId = Booked(doctorId, slot);
            var range = new[] { ("DoctorId", doctorId.ToString()), ("ExaminationTypeId", examId.ToString()),
                                ("FromDate", Day(ClinicTime.Today)), ("ToDate", Day(ClinicTime.Today.AddDays(7))) };
            var firstEntry = await AddEntryAsync(s, first, range);
            var secondEntry = await AddEntryAsync(s, second, range);

            // The same wish twice is refused.
            var dup = await s.PostAsync("/Waitlist/Add", "/Waitlist/Add", new[] { ("PinCode", first.PinCode) }.Concat(range).ToArray());
            Assert.Contains("artıq gözləmə siyahısındadır", await WebSession.TextAsync(dup));

            // Cancelling offers the time to whoever was written first, and texts them.
            var cancelled = await s.FollowAsync(await CancelAsync(s, reserveId));
            Assert.Contains($"gözləmə siyahısından {first.FullName} üçün təklif edildi", cancelled);
            var offered = Entry(firstEntry);
            Assert.Equal(WaitlistStatus.Offered, offered.Status);
            Assert.Equal(slot, offered.OfferedSlot);
            Assert.Equal(doctorId, offered.OfferedDoctorId);
            Assert.Equal(WaitlistStatus.Waiting, Entry(secondEntry).Status);
            var sms = Db(db => db.SmsMessages.Where(m => m.PatientId == first.Id && m.Kind == SmsService.KindWaitlist).ToList());
            Assert.Single(sms);
            Assert.Contains(slot.ToString("dd.MM.yyyy"), sms[0].Text);
            Assert.Contains("10:00", sms[0].Text);

            var list = await WebSession.TextAsync(await s.Client.GetAsync("/Waitlist"));
            Assert.Contains("Vaxt təklif edilib", list);
            Assert.Contains(first.FullName, list);

            // The first patient doesn't want it: back to waiting, the time goes to the second.
            var declined = await s.FollowAsync(await s.PostAsync("/Waitlist", $"/Waitlist/Decline/{firstEntry}"));
            Assert.Contains($"növbəti xəstəyə təklif edildi: {second.FullName}", declined);
            Assert.Equal(WaitlistStatus.Waiting, Entry(firstEntry).Status);
            Assert.Null(Entry(firstEntry).OfferedSlot);
            Assert.Equal(WaitlistStatus.Offered, Entry(secondEntry).Status);

            // "Rezerv et" opens the booking form with the patient, doctor, offered time and service filled in.
            var form = await WebSession.TextAsync(await s.Client.GetAsync($"/Reservation/Add?waitlistId={secondEntry}"));
            Assert.Contains("Gözləmə siyahısından rezerv", form);
            Assert.Contains($"value=\"{second.PinCode}\"", form);
            Assert.Contains("value=\"10:00\" checked", form);

            var booked = await s.PostAsync($"/Reservation/Add?waitlistId={secondEntry}", "/Reservation/Add",
                ("WaitlistId", secondEntry.ToString()), ("PinCode", second.PinCode), ("Phone", second.Phone),
                ("DoctorId", doctorId.ToString()), ("AppointmentDate", Day(slot)), ("AppointmentTime", "10:00"),
                ("ExaminationTypeIds", examId.ToString()));
            Assert.Contains("rezervasiya yaradıldı", await s.FollowAsync(booked));
            var done = Entry(secondEntry);
            Assert.Equal(WaitlistStatus.Booked, done.Status);
            var newReserve = Db(db => db.Reserves.Single(r => r.Id == done.ReserveId));
            Assert.Equal(second.Id, newReserve.PatientId);
            Assert.Equal(slot, newReserve.AppointmentAt);
        }

        [Fact]
        public async Task Only_entries_the_freed_time_suits_get_it()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var (deptId, doctorId, longExamId, a, b, c) = Db(db =>
            {
                var dept = TestData.Department(db);
                var doc = TestData.Doctor(db, dept.Id);
                var longExam = TestData.Exam(db);
                longExam.DurationMinutes = 60;
                db.SaveChanges();
                return (dept.Id, doc.Id, longExam.Id, TestData.Patient(db), TestData.Patient(db), TestData.Patient(db));
            });
            var day = ClinicTime.Today.AddDays(3);
            var freed = Booked(doctorId, day.AddHours(10));
            Booked(doctorId, day.AddHours(10.5)); // 10:30 stays busy, so only 30 minutes are free at 10:00
            var dates = new[] { ("FromDate", Day(ClinicTime.Today)), ("ToDate", Day(day.AddDays(1))) };

            // a: afternoons only; b: needs an hour; c: any doctor of the department, any time.
            var aEntry = await AddEntryAsync(s, a, dates.Append(("DoctorId", doctorId.ToString())).Append(("TimeFrom", "14:00")).ToArray());
            var bEntry = await AddEntryAsync(s, b, dates.Append(("DoctorId", doctorId.ToString())).Append(("ExaminationTypeId", longExamId.ToString())).ToArray());
            var cEntry = await AddEntryAsync(s, c, dates.Append(("DepartmentId", deptId.ToString())).ToArray());

            await CancelAsync(s, freed);
            Assert.Equal(WaitlistStatus.Waiting, Entry(aEntry).Status);
            Assert.Equal(WaitlistStatus.Waiting, Entry(bEntry).Status);
            Assert.Equal(WaitlistStatus.Offered, Entry(cEntry).Status);

            // An afternoon cancellation through the patient's SMS link reaches a.
            var afternoon = Booked(doctorId, day.AddHours(15));
            var token = Db(db => db.Reserves.Single(r => r.Id == afternoon).PatientToken!);
            var anon = _app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            var t = await WebSession.TokenAsync(anon, $"/r/{token}");
            await anon.PostAsync($"/r/{token}/cancel", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = t }));
            Assert.Equal(StatusNames.Cancelled, Db(db => db.Reserves.Include(r => r.Status).Single(r => r.Id == afternoon).Status!.Name));
            var offered = Entry(aEntry);
            Assert.Equal(WaitlistStatus.Offered, offered.Status);
            Assert.Equal(day.AddHours(15), offered.OfferedSlot);
        }

        [Fact]
        public async Task Booking_the_wished_doctor_settles_the_entry_and_bad_forms_are_refused()
        {
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);
            var (doctorId, examId, p) = Db(db => (TestData.Doctor(db).Id, TestData.Exam(db).Id, TestData.Patient(db)));
            var day = ClinicTime.Today.AddDays(4);

            // Neither doctor nor department; a new FİN without the card details; dates the wrong way round.
            var newPin = Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
            var bad = await WebSession.TextAsync(await s.PostAsync("/Waitlist/Add", "/Waitlist/Add",
                ("PinCode", newPin), ("FromDate", Day(day)), ("ToDate", Day(day.AddDays(-2)))));
            Assert.Contains("Həkim və ya şöbə seçin", bad);
            Assert.Contains("Ad daxil edilməlidir", bad);
            Assert.Contains("Son tarix başlanğıcdan əvvəl ola bilməz", bad);
            Assert.False(Db(db => db.Patients.Any(x => x.PinCode == newPin)));

            // A new patient gets a card with the entry.
            await s.PostAsync("/Waitlist/Add", "/Waitlist/Add", ("PinCode", newPin), ("Name", "Gözləyən"), ("Surname", "Yeni"),
                ("Phone", "050 444 55 66"), ("Birthday", "1985-05-05"), ("DoctorId", doctorId.ToString()),
                ("FromDate", Day(ClinicTime.Today)), ("ToDate", Day(day)));
            Assert.True(Db(db => db.WaitlistEntries.Any(w => w.Patient.PinCode == newPin && w.Status == WaitlistStatus.Waiting)));

            // Booking the patient with that doctor in the wished days (not from the list) also settles the wish.
            var entry = await AddEntryAsync(s, p, ("DoctorId", doctorId.ToString()), ("FromDate", Day(ClinicTime.Today)), ("ToDate", Day(day)));
            var booked = await s.PostAsync("/Reservation/Add", "/Reservation/Add", ("PinCode", p.PinCode), ("Phone", p.Phone),
                ("DoctorId", doctorId.ToString()), ("AppointmentDate", Day(day)), ("AppointmentTime", "11:00"), ("ExaminationTypeIds", examId.ToString()));
            Assert.Equal(HttpStatusCode.Redirect, booked.StatusCode);
            Assert.Equal(WaitlistStatus.Booked, Entry(entry).Status);

            // Closing an open entry keeps the reason.
            var other = await AddEntryAsync(s, p, ("DoctorId", doctorId.ToString()), ("FromDate", Day(ClinicTime.Today)), ("ToDate", Day(day)));
            await s.PostAsync("/Waitlist", $"/Waitlist/Close/{other}", ("note", "Başqa klinikaya getdi"));
            var closed = Entry(other);
            Assert.Equal(WaitlistStatus.Closed, closed.Status);
            Assert.Equal("Başqa klinikaya getdi", closed.CloseNote);
        }
    }
}
