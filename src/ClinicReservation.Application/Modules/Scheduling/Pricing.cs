using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Loyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>One service line: catalog price, the discount that won, what the patient pays.</summary>
    public record PriceLine(int ExaminationTypeId, decimal ListPrice, decimal Discount, string? Source, DiscountKind? Kind = null, int? RefId = null)
    {
        public decimal Price => ListPrice - Discount;

        public ReserveExaminationType ToLine() => new()
        {
            ExaminationTypeId = ExaminationTypeId, ListPrice = ListPrice, Discount = Discount, Price = Price,
            DiscountSource = Source, DiscountKind = Kind, DiscountRefId = RefId
        };
    }

    public record PriceQuote(List<PriceLine> Lines, int? FollowUpOfId, string? PromoError = null, string? PromoNote = null)
    {
        public decimal ListTotal => Lines.Sum(l => l.ListPrice);
        public decimal Discount => Lines.Sum(l => l.Discount);
        public decimal Total => Lines.Sum(l => l.Price);

        /// <summary>" Kontrol qəbulu: 25,00 ₼ endirim." — appended to the booking message.</summary>
        public string Note => (Discount > 0
            ? " " + string.Join(", ", Lines.Where(l => l.Discount > 0).GroupBy(l => l.Source).Select(g => $"{g.Key}: {Fmt.Money(g.Sum(x => x.Discount))} endirim")) + "."
            : "") + (PromoNote == null ? "" : " " + PromoNote);
    }

    /// <summary>
    /// Prices for a booking (read-only, so other modules may ask). Every discount the patient could get is
    /// weighed per service and only the best one applies — discounts don't add up. Sources: follow-up visit,
    /// campaigns, a promo code, the loyalty card level, social discounts, the employer's contract.
    /// </summary>
    public class PriceQueries
    {
        private readonly IAppDbContext _db;
        private readonly LoyaltyQueries _loyalty;
        private readonly SchedulingOptions _options;

        public PriceQueries(IAppDbContext db, LoyaltyQueries loyalty, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _loyalty = loyalty;
            _options = options.Value;
        }

        public async Task<PriceQuote> QuoteAsync(int? patientId, int? doctorId, DateTime at, IEnumerable<ExaminationType> exams, int? excludeReserveId = null,
            string? promoCode = null, CancellationToken ct = default)
        {
            var list = exams.ToList();
            var prior = await FollowUpOfAsync(patientId, doctorId, at, excludeReserveId, ct);
            var offerSet = await _loyalty.OffersAsync(patientId, at, promoCode, ct);
            var offers = await UsableAsync(offerSet.Offers, patientId, excludeReserveId, ct);
            var promoError = offerSet.PromoError;
            var promo = offerSet.Offers.FirstOrDefault(o => o.Kind == DiscountKind.Promo);
            if (promo != null && !offers.Contains(promo)) promoError = UsageError(promo, patientId == null);

            var pct = Math.Clamp(_options.FollowUpDiscountPercent, 0, 100);
            var lines = list.Select(e =>
            {
                var candidates = new List<(decimal Amount, string Source, DiscountKind Kind, int? Ref)>();
                if (prior != null && pct > 0 && e.FollowUpDiscount && e.Price > 0)
                    candidates.Add((Math.Round(e.Price * pct / 100m, 2), pct == 100 ? "Kontrol qəbulu (pulsuz)" : $"Kontrol qəbulu (−{pct:0.##}%)", DiscountKind.FollowUp, prior.Id));
                foreach (var o in offers)
                    if (o.AmountFor(e) is var a && a > 0) candidates.Add((a, Label(o), o.Kind, o.RefId));
                var best = candidates.OrderByDescending(c => c.Amount).ThenBy(c => c.Kind).FirstOrDefault();
                return best.Amount > 0
                    ? new PriceLine(e.Id, e.Price, best.Amount, best.Source, best.Kind, best.Ref)
                    : new PriceLine(e.Id, e.Price, 0, null);
            }).ToList();

            string? promoNote = null;
            if (promo != null && promoError == null && lines.All(l => l.Kind != DiscountKind.Promo))
                promoNote = list.Any(promo.Covers)
                    ? $"Promo kod {promoCode!.Trim().ToUpperInvariant()} tətbiq olunmadı — xəstənin daha sərfəli endirimi var."
                    : $"Promo kod {promoCode!.Trim().ToUpperInvariant()} seçilən xidmətlərə aid deyil.";
            return new PriceQuote(lines, prior?.Id, promoError, promoNote);
        }

        /// <summary>The booking form's live preview: by FİN (unknown = new patient), the chosen services, time and code.</summary>
        public async Task<PriceQuote> PreviewAsync(string? pin, int? doctorId, DateTime at, IReadOnlyCollection<int> examIds, string? promoCode, CancellationToken ct = default)
        {
            var p = Patient.NormalizePin(pin);
            var patientId = p.Length == 0 ? null : await _db.Patients.Where(x => x.PinCode == p).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            var exams = await _db.ExaminationTypes.AsNoTracking().Where(e => examIds.Contains(e.Id)).ToListAsync(ct);
            return await QuoteAsync(patientId, doctorId, at, exams, null, promoCode, ct);
        }

        private static string Label(DiscountOffer o)
        {
            var size = o.Type == DiscountType.Percent ? $"−{o.Value:0.##}%" : $"−{Fmt.Money(o.Value)}";
            return o.Kind switch
            {
                DiscountKind.Campaign => $"Kampaniya: {o.Label} ({size})",
                DiscountKind.Promo => $"{o.Label} ({size})",
                DiscountKind.Loyalty => $"{o.Label} ({size})",
                DiscountKind.Social => $"Sosial güzəşt: {o.Label} ({size})",
                DiscountKind.Corporate => $"Korporativ: {o.Label} ({size})",
                _ => $"{o.Label} ({size})"
            };
        }

        private static string UsageError(DiscountOffer promo, bool newPatient) =>
            promo.NewPatientsOnly && !newPatient ? "Bu promo kod yalnız yeni xəstələr üçündür" : "Bu promo kodun istifadə limiti dolub";

        /// <summary>Drops campaigns over their use limits, and «new patients only» ones for returning patients.</summary>
        private async Task<List<DiscountOffer>> UsableAsync(List<DiscountOffer> offers, int? patientId, int? excludeReserveId, CancellationToken ct)
        {
            var campaignKinds = new[] { DiscountKind.Campaign, DiscountKind.Promo };
            var limited = offers.Where(o => campaignKinds.Contains(o.Kind) && (o.MaxUses != null || o.MaxUsesPerPatient != null)).Select(o => o.RefId).ToList();
            var uses = new Dictionary<int, (int All, int Mine)>();
            if (limited.Count > 0)
            {
                var rows = await _db.ReserveExaminationTypes
                    .Where(l => l.DiscountRefId != null && limited.Contains(l.DiscountRefId.Value)
                                && (l.DiscountKind == DiscountKind.Campaign || l.DiscountKind == DiscountKind.Promo)
                                && l.ReserveId != excludeReserveId && l.Reserve.Status!.Name != StatusNames.Cancelled)
                    // A course is one purchase: its sessions count as one use.
                    .Select(l => new { Ref = l.DiscountRefId!.Value, Key = l.Reserve.SeriesId != null ? -l.Reserve.SeriesId!.Value : l.ReserveId, l.Reserve.PatientId })
                    .Distinct().ToListAsync(ct);
                foreach (var g in rows.GroupBy(r => r.Ref))
                    uses[g.Key] = (g.Select(x => x.Key).Distinct().Count(), g.Where(x => x.PatientId == patientId).Select(x => x.Key).Distinct().Count());
            }
            var returning = patientId != null && offers.Any(o => o.NewPatientsOnly)
                && await _db.Reserves.AnyAsync(r => r.PatientId == patientId && r.Id != excludeReserveId && r.Status!.Name == StatusNames.Completed, ct);

            return offers.Where(o =>
            {
                if (o.NewPatientsOnly && returning) return false;
                uses.TryGetValue(o.RefId, out var u);
                return (o.MaxUses == null || u.All < o.MaxUses) && (o.MaxUsesPerPatient == null || u.Mine < o.MaxUsesPerPatient);
            }).ToList();
        }

        /// <summary>The completed visit with the same doctor, within the follow-up window before <paramref name="at"/>.</summary>
        public async Task<Reserve?> FollowUpOfAsync(int? patientId, int? doctorId, DateTime at, int? excludeReserveId = null, CancellationToken ct = default)
        {
            if (patientId == null || doctorId == null || _options.FollowUpWindowDays <= 0) return null;
            var since = at.Date.AddDays(-_options.FollowUpWindowDays);
            return await _db.Reserves.AsNoTracking()
                .Where(r => r.PatientId == patientId && r.DoctorId == doctorId && r.Id != excludeReserveId
                            && r.Status!.Name == StatusNames.Completed && r.AppointmentAt < at && r.AppointmentAt >= since)
                .OrderByDescending(r => r.AppointmentAt)
                .FirstOrDefaultAsync(ct);
        }
    }
}
