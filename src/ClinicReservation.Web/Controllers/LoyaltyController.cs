using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Loyalty;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Loyallıq və endirimlər: cards, levels, campaigns / promo codes, social discounts, companies.</summary>
    [HasPermission(Permissions.PatientsView, Permissions.LoyaltyManage)]
    public class LoyaltyController : Controller
    {
        private readonly LoyaltyQueries _queries;
        private readonly LoyaltyCards _cards;
        private readonly DiscountCatalog _catalog;

        public LoyaltyController(LoyaltyQueries queries, LoyaltyCards cards, DiscountCatalog catalog)
        {
            _queries = queries;
            _cards = cards;
            _catalog = catalog;
        }

        /// <summary>tab: cards | tiers | campaigns | social | companies.</summary>
        public async Task<IActionResult> Index(string? tab, string? q, CancellationToken ct)
        {
            tab = tab is "tiers" or "campaigns" or "social" or "companies" ? tab : "cards";
            ViewBag.Tab = tab;
            ViewBag.Query = q;
            ViewBag.Cards = tab == "cards" ? await _queries.CardsAsync(q, ct) : new List<CardView>();
            ViewBag.Tiers = await _queries.TiersAsync(ct);
            ViewBag.Campaigns = tab == "campaigns" ? await _queries.CampaignsAsync(ct) : new List<Campaign>();
            ViewBag.Categories = tab == "social" ? await _queries.BenefitCategoriesAsync(ct) : new List<BenefitCategory>();
            ViewBag.Companies = tab == "companies" ? await _queries.CompaniesAsync(ct) : new List<Company>();
            return View();
        }

        // ----- Cards -----
        public async Task<IActionResult> Card(int id, CancellationToken ct)
        {
            var card = await _queries.CardAsync(id, ct);
            if (card == null) return NotFound();
            ViewBag.History = await _queries.HistoryAsync(id, ct);
            ViewBag.Tiers = await _queries.TiersAsync(ct);
            return View(card);
        }

        public async Task<IActionResult> CardPrint(int id, CancellationToken ct) =>
            await _queries.CardAsync(id, ct) is { } card ? View(card) : NotFound();

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.PatientsEdit)]
        public async Task<IActionResult> AddMember(int id, string? pin, CancellationToken ct) => Back(await _cards.AddMemberAsync(id, pin, ct), id);

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.PatientsEdit)]
        public async Task<IActionResult> RemoveMember(int id, int patientId, CancellationToken ct) => Back(await _cards.RemoveMemberAsync(id, patientId, ct), id);

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> AdjustPoints(int id, int points, string? note, CancellationToken ct) => Back(await _cards.AdjustPointsAsync(id, points, note, ct), id);

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> SetTier(int id, int? tierId, CancellationToken ct) => Back(await _cards.SetTierAsync(id, tierId, ct), id);

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> SetActive(int id, bool active, CancellationToken ct) => Back(await _cards.SetActiveAsync(id, active, ct), id);

        // ----- Catalogs -----
        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> SaveTier(int id, string? name, decimal minSpend, decimal discountPercent, decimal pointsPercent, bool isActive, CancellationToken ct) =>
            Tab(await _catalog.SaveTierAsync(id, name, minSpend, discountPercent, pointsPercent, isActive, ct), "tiers");

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> SaveCategory(int id, string? name, decimal percent, bool isActive, string? note, CancellationToken ct) =>
            Tab(await _catalog.SaveBenefitCategoryAsync(id, name, percent, isActive, note, ct), "social");

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> SaveCompany(int id, string? name, string? contractNo, decimal discountPercent, DateTime? contractFrom, DateTime? contractUntil,
            string? contact, bool isActive, CancellationToken ct) =>
            Tab(await _catalog.SaveCompanyAsync(id, name, contractNo, discountPercent, contractFrom, contractUntil, contact, isActive, ct), "companies");

        [HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> Campaign(int? id, [FromServices] ClinicReservation.Modules.Scheduling.ReferralQueries lists, CancellationToken ct)
        {
            var model = new CampaignFormViewModel { StartsOn = ClinicTime.Today, EndsOn = ClinicTime.Today.AddMonths(1) };
            if (id != null)
            {
                var c = await _queries.CampaignAsync(id.Value, ct);
                if (c == null) return NotFound();
                model = new CampaignFormViewModel
                {
                    Id = c.Id, Name = c.Name, Code = c.Code, Type = c.Type, Value = c.Value, StartsOn = c.StartsOn, EndsOn = c.EndsOn,
                    DepartmentId = c.DepartmentId, ExamIds = c.Exams.Select(x => x.ExaminationTypeId).ToList(), MaxUses = c.MaxUses,
                    MaxUsesPerPatient = c.MaxUsesPerPatient, NewPatientsOnly = c.NewPatientsOnly, IsActive = c.IsActive, Note = c.Note
                };
            }
            return await CampaignView(model, lists, ct);
        }

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.LoyaltyManage)]
        public async Task<IActionResult> Campaign(CampaignFormViewModel model, [FromServices] ClinicReservation.Modules.Scheduling.ReferralQueries lists, CancellationToken ct)
        {
            var r = await _catalog.SaveCampaignAsync(model, ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList(), ct);
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Index), new { tab = "campaigns" });
            }
            foreach (var (field, message) in r.Errors)
                if (!(ModelState.TryGetValue(field ?? "", out var e) && e.Errors.Any(x => x.ErrorMessage == message)))
                    ModelState.AddModelError(field ?? "", message);
            return await CampaignView(model, lists, ct);
        }

        private async Task<IActionResult> CampaignView(CampaignFormViewModel model, ClinicReservation.Modules.Scheduling.ReferralQueries lists, CancellationToken ct)
        {
            var (departments, _, exams) = await lists.FormListsAsync(ct);
            ViewBag.Departments = departments;
            ViewBag.Exams = exams;
            return View("Campaign", model);
        }

        private IActionResult Back(Result? r, int cardId)
        {
            if (r == null) return NotFound();
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Card), new { id = cardId });
        }

        private IActionResult Tab(Result r, string tab)
        {
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Index), new { tab });
        }
    }
}
