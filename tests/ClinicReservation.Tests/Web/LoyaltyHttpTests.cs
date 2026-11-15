using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>Spec E1–E7: discounts don't add up (best one per service), promo limits, points, family card, manual discount limits.</summary>
    [Collection(DbCollection.Name)]
    public class LoyaltyHttpTests
    {
        private readonly AppFixture _app;
        public LoyaltyHttpTests(AppFixture app) => _app = app;

        private T Db<T>(Func<AppDbContext, T> query)
        {
            using var scope = _app.Scope();
            return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");
        private static int _hour = 8;

        /// <summary>Books the services for the patient (next free hour of a fresh doctor) and returns the response.</summary>
        private async Task<HttpResponseMessage> BookAsync(WebSession s, Patient p, int doctorId, string? promo, params int[] exams)
        {
            var fields = new List<(string, string)> { ("PinCode", p.PinCode), ("Phone", p.Phone), ("DoctorId", doctorId.ToString()),
                ("AppointmentDate", D(ClinicTime.Today.AddDays(2))), ("AppointmentTime", $"{(_hour++ % 11) + 8:00}:00") };
            if (promo != null) fields.Add(("PromoCode", promo));
            fields.AddRange(exams.Select(e => ("ExaminationTypeIds", e.ToString())));
            return await s.PostAsync("/Reservation/Add", "/Reservation/Add", fields.ToArray());
        }

        private static int IdOf(HttpResponseMessage r) => int.Parse(r.Headers.Location!.ToString().Split('/').Last());
        private List<ReserveExaminationType> Lines(int reserveId) => Db(db => db.ReserveExaminationTypes.AsNoTracking().Where(x => x.ReserveId == reserveId).ToList());

        [Fact]
        public async Task The_best_single_discount_wins_per_service_and_expired_or_limited_ones_do_not_apply()
        {
            var (p, doctorId, x, y, companyId) = Db(db =>
            {
                var p = TestData.Patient(db);
                var x = TestData.Exam(db); x.Price = 100;
                var y = TestData.Exam(db); y.Price = 50;
                var platinum = db.LoyaltyTiers.Single(t => t.Name == "Platin"); // 15%
                var card = new LoyaltyCard { Number = TestData.Unique("T"), HolderPatientId = p.Id, TierOverrideId = platinum.Id, CreatedAt = ClinicTime.Now };
                card.Members.Add(new LoyaltyCardMember { PatientId = p.Id, JoinedAt = ClinicTime.Now });
                db.LoyaltyCards.Add(card);
                var company = new Company { Name = TestData.Unique("Şirkət "), DiscountPercent = 12 };
                db.Companies.Add(company);
                db.SaveChanges();
                db.PatientCompanies.Add(new PatientCompany { PatientId = p.Id, CompanyId = company.Id, AddedAt = ClinicTime.Now });
                // An expired 50% social discount must not count.
                db.PatientBenefits.Add(new PatientBenefit { PatientId = p.Id, CategoryId = db.BenefitCategories.First(c => c.Percent == 50).Id, DocumentNo = "V-1", ValidUntil = ClinicTime.Today.AddDays(-1), AddedAt = ClinicTime.Now });
                var campaign = new Campaign { Name = TestData.Unique("Payız "), Type = DiscountType.Percent, Value = 20, StartsOn = ClinicTime.Today, EndsOn = ClinicTime.Today.AddDays(30) };
                campaign.Exams.Add(new CampaignExam { ExaminationTypeId = x.Id });
                db.Campaigns.Add(campaign);
                db.SaveChanges();
                return (p, TestData.Doctor(db).Id, x.Id, y.Id, company.Id);
            });
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);

            // X: campaign 20% beats card 15% and company 12%; Y (no campaign): card 15%.
            var booked = await BookAsync(s, p, doctorId, null, x, y);
            Assert.Equal(HttpStatusCode.Redirect, booked.StatusCode);
            var lines = Lines(IdOf(booked));
            var lx = lines.Single(l => l.ExaminationTypeId == x);
            var ly = lines.Single(l => l.ExaminationTypeId == y);
            Assert.Equal((100m, 20m, 80m, DiscountKind.Campaign), (lx.ListPrice, lx.Discount, lx.Price, lx.DiscountKind!.Value));
            Assert.Equal((50m, 7.5m, 42.5m, DiscountKind.Loyalty), (ly.ListPrice, ly.Discount, ly.Price, ly.DiscountKind!.Value));
            Assert.Contains("Platin kart", ly.DiscountSource);
            var details = await s.FollowAsync(booked);
            Assert.Contains("Kampaniya:", details);

            // The live preview says the same.
            var quote = await s.Client.GetStringAsync($"/Reservation/Quote?pin={p.PinCode}&doctorId={doctorId}&date={D(ClinicTime.Today.AddDays(3))}&examTypeIds={x}&examTypeIds={y}");
            Assert.Contains("\"discount\":27.5", quote);

            // The company contract expires → the card still wins on Y, nothing changes; company alone would give 12%.
            Db(db => { db.Companies.Single(c => c.Id == companyId).ContractUntil = ClinicTime.Today.AddDays(-1); return db.SaveChanges(); });
            var again = Lines(IdOf(await BookAsync(s, p, doctorId, null, y)));
            Assert.Equal(DiscountKind.Loyalty, again.Single().DiscountKind);
        }

        [Fact]
        public async Task Promo_codes_are_checked_limited_per_patient_and_released_when_the_visit_is_cancelled()
        {
            var code = "YAY" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var newOnly = "NEW" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var (p, doctorId, x) = Db(db =>
            {
                var p = TestData.Patient(db);
                var x = TestData.Exam(db); x.Price = 80;
                db.Campaigns.Add(new Campaign { Name = "Yay", Code = code, Type = DiscountType.Amount, Value = 30, StartsOn = ClinicTime.Today, EndsOn = ClinicTime.Today.AddDays(10), MaxUsesPerPatient = 1 });
                db.Campaigns.Add(new Campaign { Name = "Yeni", Code = newOnly, Type = DiscountType.Percent, Value = 50, StartsOn = ClinicTime.Today, EndsOn = ClinicTime.Today.AddDays(10), NewPatientsOnly = true });
                var done = TestData.Reserve(db, p, StatusNames.Completed, x);
                done.AppointmentAt = ClinicTime.Today.AddDays(-40);
                db.SaveChanges();
                return (p, TestData.Doctor(db).Id, x.Id);
            });
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception);

            Assert.Contains("Belə promo kod yoxdur", await WebSession.TextAsync(await BookAsync(s, p, doctorId, "YOXDUR123", x)));
            Assert.Contains("yalnız yeni xəstələr üçündür", await WebSession.TextAsync(await BookAsync(s, p, doctorId, newOnly, x)));

            var first = await BookAsync(s, p, doctorId, code.ToLower(), x);
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
            var line = Lines(IdOf(first)).Single();
            Assert.Equal((30m, 50m, DiscountKind.Promo), (line.Discount, line.Price, line.DiscountKind!.Value));

            // Once per patient…
            Assert.Contains("istifadə limiti dolub", await WebSession.TextAsync(await BookAsync(s, p, doctorId, code, x)));
            // …unless that visit is cancelled.
            var id = IdOf(first);
            await s.PostAsync($"/Reservation/Details/{id}", "/Reservation/Cancel", ("Id", id.ToString()), ("ReasonId", Db(TestData.ReasonId).ToString()));
            Assert.Equal(HttpStatusCode.Redirect, (await BookAsync(s, p, doctorId, code, x)).StatusCode);
        }

        [Fact]
        public async Task Payments_earn_points_family_members_share_them_and_points_pay_at_the_cashier()
        {
            var (holder, relative, doctorId, x) = Db(db =>
            {
                var x = TestData.Exam(db); x.Price = 300;
                db.SaveChanges();
                return (TestData.Patient(db), TestData.Patient(db), TestData.Doctor(db).Id, x.Id);
            });
            var reception = await WebSession.LoginAsync(_app, AppRoles.Reception);
            await reception.PostAsync($"/Patient/Details/{holder.Id}", $"/Patient/IssueCard/{holder.Id}", ("family", "true"));
            var cardId = Db(db => db.LoyaltyCardMembers.Single(m => m.PatientId == holder.Id).CardId);
            await reception.PostAsync($"/Loyalty/Card/{cardId}", $"/Loyalty/AddMember/{cardId}", ("pin", relative.PinCode));
            Assert.Equal(2, Db(db => db.LoyaltyCardMembers.Count(m => m.CardId == cardId)));
            // A patient belongs to one card only.
            Assert.Contains("artıq bir kartın üzvüdür", await reception.FollowAsync(await reception.PostAsync($"/Loyalty/Card/{cardId}", $"/Loyalty/AddMember/{cardId}", ("pin", relative.PinCode))));

            // The relative pays 300 ₼ in cash: Standart level earns 1% → 300 points (1 bal = 0,01 ₼) on the shared card.
            var visit = IdOf(await BookAsync(reception, relative, doctorId, null, x));
            var cashier = await WebSession.LoginAsync(_app, AppRoles.Cashier);
            var page = $"/Kassa/Pay/{visit}";
            await cashier.PostAsync(page, "/Kassa/Pay", ("ReserveId", visit.ToString()), ("Amount", "300"), ("Method", "Cash"));
            int Points() => Db(db => db.LoyaltyTransactions.Where(t => t.CardId == cardId).Sum(t => t.Points));
            Assert.Equal(300, Points());
            Assert.Equal(300m, Db(db => db.LoyaltyTransactions.Where(t => t.CardId == cardId).Sum(t => t.Spend)));

            // The holder pays part of a visit with points: too few / too many are refused.
            var second = IdOf(await BookAsync(reception, holder, doctorId, null, x));
            var page2 = $"/Kassa/Pay/{second}";
            Assert.Contains("Ən azı 100 bal", await WebSession.TextAsync(await cashier.PostAsync(page2, "/Kassa/Pay", ("ReserveId", second.ToString()), ("Amount", "0.50"), ("Method", "Points"))));
            Assert.Contains("Kartda 300 bal", await WebSession.TextAsync(await cashier.PostAsync(page2, "/Kassa/Pay", ("ReserveId", second.ToString()), ("Amount", "5"), ("Method", "Points"))));
            Assert.Equal(HttpStatusCode.Redirect, (await cashier.PostAsync(page2, "/Kassa/Pay", ("ReserveId", second.ToString()), ("Amount", "2"), ("Method", "Points"))).StatusCode);
            Assert.Equal(100, Points());
            // Paying with points earns nothing; 1 ₼ in cash earns 1 point.
            await cashier.PostAsync(page2, "/Kassa/Pay", ("ReserveId", second.ToString()), ("Amount", "1"), ("Method", "Cash"));
            Assert.Equal(101, Points());
            // Cancelled: 3 ₼ to give back, but only the 2 ₼ paid with points can go back as points.
            await reception.PostAsync($"/Reservation/Details/{second}", "/Reservation/Cancel", ("Id", second.ToString()), ("ReasonId", Db(TestData.ReasonId).ToString()));
            Assert.Contains("Bal ilə ən çox 2,00", await WebSession.TextAsync(await cashier.PostAsync(page2, "/Kassa/Refund", ("ReserveId", second.ToString()), ("Amount", "3"), ("Method", "Points"))));
            await cashier.PostAsync(page2, "/Kassa/Refund", ("ReserveId", second.ToString()), ("Amount", "2"), ("Method", "Points"));
            Assert.Equal(301, Points());

            // Paying 300 more moves the card to «Gümüş» (500 ₼ in 12 months): 5% off from now on.
            var third = IdOf(await BookAsync(reception, holder, doctorId, null, x));
            await cashier.PostAsync($"/Kassa/Pay/{third}", "/Kassa/Pay", ("ReserveId", third.ToString()), ("Amount", "300"), ("Method", "Card"));
            var fourth = Lines(IdOf(await BookAsync(reception, relative, doctorId, null, x))).Single();
            Assert.Equal((15m, DiscountKind.Loyalty), (fourth.Discount, fourth.DiscountKind!.Value));
            Assert.Contains("Gümüş", await WebSession.TextAsync(await reception.Client.GetAsync($"/Loyalty/Card/{cardId}")));
        }

        [Fact]
        public async Task Manual_discount_needs_a_reason_stays_within_the_role_limit_and_never_lowers_a_bigger_one()
        {
            var (p, doctorId, x, y) = Db(db =>
            {
                var x = TestData.Exam(db); x.Price = 100;
                var y = TestData.Exam(db); y.Price = 100;
                var p = TestData.Patient(db);
                db.SaveChanges();
                db.PatientBenefits.Add(new PatientBenefit { PatientId = p.Id, CategoryId = db.BenefitCategories.First(c => c.Name == "Tələbə").Id, DocumentNo = "S-9", AddedAt = ClinicTime.Now });
                var campaign = new Campaign { Name = TestData.Unique("K"), Type = DiscountType.Percent, Value = 30, StartsOn = ClinicTime.Today, EndsOn = ClinicTime.Today.AddDays(5) };
                campaign.Exams.Add(new CampaignExam { ExaminationTypeId = x.Id });
                db.Campaigns.Add(campaign);
                db.SaveChanges();
                return (p, TestData.Doctor(db).Id, x.Id, y.Id);
            });
            var s = await WebSession.LoginAsync(_app, AppRoles.Reception); // limit 10%
            var id = IdOf(await BookAsync(s, p, doctorId, null, x, y));      // X: campaign 30, Y: student 5
            var page = $"/Reservation/Details/{id}";

            Assert.Contains("səbəbini yazın", await s.FollowAsync(await s.PostAsync(page, $"/Reservation/Discount/{id}", ("type", "Percent"), ("value", "10"), ("reason", ""))));
            Assert.Contains("limitiniz 10%", await s.FollowAsync(await s.PostAsync(page, $"/Reservation/Discount/{id}", ("type", "Percent"), ("value", "15"), ("reason", "şikayət"))));

            Assert.Contains("Endirim tətbiq edildi (1 xidmət)", await s.FollowAsync(await s.PostAsync(page, $"/Reservation/Discount/{id}", ("type", "Percent"), ("value", "10"), ("reason", "şikayət"))));
            var lines = Lines(id);
            Assert.Equal((30m, DiscountKind.Campaign), (lines.Single(l => l.ExaminationTypeId == x).Discount, lines.Single(l => l.ExaminationTypeId == x).DiscountKind!.Value));
            Assert.Equal((10m, DiscountKind.Manual), (lines.Single(l => l.ExaminationTypeId == y).Discount, lines.Single(l => l.ExaminationTypeId == y).DiscountKind!.Value));
            Assert.True(Db(db => db.AuditLogs.Any(a => a.EntityId == id.ToString() && a.Action == "Əl ilə endirim")));

            // The limit is set per role on the Roles page.
            var admin = await WebSession.LoginAsync(_app, AppRoles.Admin);
            var roleId = Db(db => db.Roles.Single(r => r.Name == AppRoles.Reception).Id);
            var perms = Db(db => db.RoleClaims.Where(c => c.RoleId == roleId && c.ClaimType == Permissions.ClaimType).Select(c => c.ClaimValue!).ToList());
            await admin.PostAsync("/Role", "/Role/Save", perms.Select(v => ("permissions", v)).Append(("roleId", roleId)).Append(("discountLimit", "25")).ToArray());
            Assert.Equal("25", Db(db => db.RoleClaims.Single(c => c.RoleId == roleId && c.ClaimType == Permissions.DiscountLimitClaim).ClaimValue));
            var again = await WebSession.LoginAsync(_app, AppRoles.Reception);
            Assert.Contains("Endirim tətbiq edildi", await again.FollowAsync(await again.PostAsync(page, $"/Reservation/Discount/{id}", ("type", "Percent"), ("value", "20"), ("reason", "rəhbərin razılığı"))));
            Assert.Equal(20m, Lines(id).Single(l => l.ExaminationTypeId == y).Discount);
            // Put the default back for the other tests.
            await admin.PostAsync("/Role", "/Role/Save", perms.Select(v => ("permissions", v)).Append(("roleId", roleId)).Append(("discountLimit", "10")).ToArray());
        }
    }
}
