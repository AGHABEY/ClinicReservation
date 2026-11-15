using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Loyalty
{
    /// <summary>One discount a patient could get. Scope: a department's services and/or chosen ones (empty = all).</summary>
    public record DiscountOffer(DiscountKind Kind, int RefId, string Label, DiscountType Type, decimal Value,
        int? DepartmentId = null, IReadOnlySet<int>? ExamIds = null, int? MaxUses = null, int? MaxUsesPerPatient = null, bool NewPatientsOnly = false)
    {
        public bool Covers(ExaminationType e) =>
            (DepartmentId == null || e.DepartmentId == DepartmentId) && (ExamIds == null || ExamIds.Count == 0 || ExamIds.Contains(e.Id));

        /// <summary>₼ off this service (never more than its price).</summary>
        public decimal AmountFor(ExaminationType e) =>
            !Covers(e) || e.Price <= 0 ? 0 : Type == DiscountType.Percent ? Math.Round(e.Price * Math.Clamp(Value, 0, 100) / 100m, 2) : Math.Min(Value, e.Price);
    }

    public record OfferSet(List<DiscountOffer> Offers, string? PromoError);

    /// <summary>A card as reception sees it: level (fixed or by spending), points, members.</summary>
    public record CardView(LoyaltyCard Card, LoyaltyTier? Tier, LoyaltyTier? NextTier, decimal Spend12m, int Points, decimal PointsMoney);

    public class LoyaltyQueries
    {
        private readonly IAppDbContext _db;
        private readonly LoyaltyOptions _options;

        public LoyaltyQueries(IAppDbContext db, IOptions<LoyaltyOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public LoyaltyOptions Options => _options;

        public Task<List<LoyaltyTier>> TiersAsync(CancellationToken ct = default) =>
            _db.LoyaltyTiers.AsNoTracking().OrderBy(t => t.MinSpend).ToListAsync(ct);

        public async Task<CardView?> CardForPatientAsync(int patientId, CancellationToken ct = default)
        {
            var cardId = await _db.LoyaltyCardMembers.Where(m => m.PatientId == patientId).Select(m => (int?)m.CardId).FirstOrDefaultAsync(ct);
            return cardId == null ? null : await CardAsync(cardId.Value, ct);
        }

        public async Task<CardView?> CardByNumberAsync(string? number, CancellationToken ct = default)
        {
            var n = (number ?? "").Trim().ToUpperInvariant();
            var id = await _db.LoyaltyCards.Where(c => c.Number == n).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
            return id == null ? null : await CardAsync(id.Value, ct);
        }

        public async Task<CardView?> CardAsync(int cardId, CancellationToken ct = default)
        {
            var card = await _db.LoyaltyCards.AsNoTracking().Include(c => c.Holder).Include(c => c.TierOverride)
                .Include(c => c.Members).ThenInclude(m => m.Patient)
                .FirstOrDefaultAsync(c => c.Id == cardId, ct);
            if (card == null) return null;
            var since = ClinicTime.Now.AddMonths(-12);
            var spend = await _db.LoyaltyTransactions.Where(t => t.CardId == cardId && t.At >= since).SumAsync(t => (decimal?)t.Spend, ct) ?? 0;
            var points = await _db.LoyaltyTransactions.Where(t => t.CardId == cardId).SumAsync(t => (int?)t.Points, ct) ?? 0;
            var tiers = (await TiersAsync(ct)).Where(t => t.IsActive).ToList();
            var tier = card.TierOverride ?? tiers.Where(t => t.MinSpend <= spend).OrderByDescending(t => t.MinSpend).FirstOrDefault();
            var next = card.TierOverride != null ? null : tiers.Where(t => t.MinSpend > spend).OrderBy(t => t.MinSpend).FirstOrDefault();
            return new CardView(card, tier, next, spend, points, points * _options.PointValue);
        }

        public Task<List<LoyaltyTransaction>> HistoryAsync(int cardId, CancellationToken ct = default) =>
            _db.LoyaltyTransactions.AsNoTracking().Where(t => t.CardId == cardId).OrderByDescending(t => t.At).Take(50).ToListAsync(ct);

        public async Task<List<CardView>> CardsAsync(string? q, CancellationToken ct = default)
        {
            var query = _db.LoyaltyCards.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim().ToLower();
                query = query.Where(c => c.Number.ToLower().Contains(t) || c.Members.Any(m => (m.Patient.Name + " " + m.Patient.Surname).ToLower().Contains(t) || m.Patient.PinCode.ToLower().Contains(t)));
            }
            var ids = await query.OrderByDescending(c => c.CreatedAt).Select(c => c.Id).Take(200).ToListAsync(ct);
            var list = new List<CardView>();
            foreach (var id in ids) if (await CardAsync(id, ct) is { } v) list.Add(v);
            return list;
        }

        // ----- Catalogs -----
        public Task<List<Campaign>> CampaignsAsync(CancellationToken ct = default) =>
            _db.Campaigns.AsNoTracking().Include(c => c.Department).Include(c => c.Exams).ThenInclude(x => x.ExaminationType)
                .OrderByDescending(c => c.IsActive).ThenByDescending(c => c.EndsOn).ToListAsync(ct);

        public Task<Campaign?> CampaignAsync(int id, CancellationToken ct = default) =>
            _db.Campaigns.AsNoTracking().Include(c => c.Exams).FirstOrDefaultAsync(c => c.Id == id, ct);

        public Task<List<BenefitCategory>> BenefitCategoriesAsync(CancellationToken ct = default) =>
            _db.BenefitCategories.AsNoTracking().OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ToListAsync(ct);

        public Task<List<Company>> CompaniesAsync(CancellationToken ct = default) =>
            _db.Companies.AsNoTracking().OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ToListAsync(ct);

        public Task<List<PatientBenefit>> BenefitsOfAsync(int patientId, CancellationToken ct = default) =>
            _db.PatientBenefits.AsNoTracking().Include(b => b.Category).Where(b => b.PatientId == patientId && b.IsActive).OrderBy(b => b.AddedAt).ToListAsync(ct);

        public Task<PatientCompany?> CompanyOfAsync(int patientId, CancellationToken ct = default) =>
            _db.PatientCompanies.AsNoTracking().Include(x => x.Company).FirstOrDefaultAsync(x => x.PatientId == patientId, ct);

        /// <summary>
        /// Every discount this patient could get on <paramref name="at"/>: running campaigns, the promo code (if given),
        /// the card level, valid social discounts, the employer's contract. Pricing picks the best per service.
        /// </summary>
        public async Task<OfferSet> OffersAsync(int? patientId, DateTime at, string? promoCode, CancellationToken ct = default)
        {
            var day = at.Date;
            var offers = new List<DiscountOffer>();
            string? promoError = null;

            var campaigns = await _db.Campaigns.AsNoTracking().Include(c => c.Exams)
                .Where(c => c.IsActive && c.StartsOn <= day && c.EndsOn >= day).ToListAsync(ct);
            DiscountOffer FromCampaign(Campaign c, DiscountKind kind) =>
                new(kind, c.Id, kind == DiscountKind.Promo ? $"Promo kod {c.Code}" : c.Name, c.Type, c.Value, c.DepartmentId,
                    c.Exams.Select(x => x.ExaminationTypeId).ToHashSet(), c.MaxUses, c.MaxUsesPerPatient, c.NewPatientsOnly);
            offers.AddRange(campaigns.Where(c => c.Code == null).Select(c => FromCampaign(c, DiscountKind.Campaign)));

            var code = string.IsNullOrWhiteSpace(promoCode) ? null : promoCode.Trim().ToUpperInvariant();
            if (code != null)
            {
                var promo = campaigns.FirstOrDefault(c => c.Code == code);
                if (promo != null) offers.Add(FromCampaign(promo, DiscountKind.Promo));
                else promoError = await _db.Campaigns.AnyAsync(c => c.Code == code, ct)
                    ? "Bu promo kodun müddəti bitib və ya aktiv deyil"
                    : "Belə promo kod yoxdur";
            }

            if (patientId != null)
            {
                if (await CardForPatientAsync(patientId.Value, ct) is { Card.IsActive: true, Tier: { DiscountPercent: > 0 } tier } card)
                    offers.Add(new DiscountOffer(DiscountKind.Loyalty, card.Card.Id, $"{tier.Name} kart", DiscountType.Percent, tier.DiscountPercent));

                var benefits = await _db.PatientBenefits.AsNoTracking().Include(b => b.Category)
                    .Where(b => b.PatientId == patientId && b.IsActive && b.Category.IsActive && (b.ValidUntil == null || b.ValidUntil >= day))
                    .ToListAsync(ct);
                offers.AddRange(benefits.Select(b => new DiscountOffer(DiscountKind.Social, b.Id, b.Category.Name, DiscountType.Percent, b.Category.Percent)));

                var job = await _db.PatientCompanies.AsNoTracking().Include(x => x.Company)
                    .FirstOrDefaultAsync(x => x.PatientId == patientId && x.Company.IsActive
                        && (x.Company.ContractFrom == null || x.Company.ContractFrom <= day) && (x.Company.ContractUntil == null || x.Company.ContractUntil >= day), ct);
                if (job is { Company.DiscountPercent: > 0 })
                    offers.Add(new DiscountOffer(DiscountKind.Corporate, job.CompanyId, job.Company.Name, DiscountType.Percent, job.Company.DiscountPercent));
            }
            return new OfferSet(offers, promoError);
        }

        /// <summary>Points the patient's card can spend now (0 = no card).</summary>
        public async Task<(int? CardId, int Points)> BalanceAsync(int patientId, CancellationToken ct = default)
        {
            var cardId = await _db.LoyaltyCardMembers.Where(m => m.PatientId == patientId && m.Card.IsActive).Select(m => (int?)m.CardId).FirstOrDefaultAsync(ct);
            if (cardId == null) return (null, 0);
            return (cardId, await _db.LoyaltyTransactions.Where(t => t.CardId == cardId).SumAsync(t => (int?)t.Points, ct) ?? 0);
        }
    }
}
