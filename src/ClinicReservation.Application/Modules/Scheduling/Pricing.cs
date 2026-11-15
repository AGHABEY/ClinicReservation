using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>One service line: catalog price, discount, what the patient pays, and why.</summary>
    public record PriceLine(int ExaminationTypeId, decimal ListPrice, decimal Discount, string? Source)
    {
        public decimal Price => ListPrice - Discount;

        public ReserveExaminationType ToLine() =>
            new() { ExaminationTypeId = ExaminationTypeId, ListPrice = ListPrice, Discount = Discount, Price = Price, DiscountSource = Source };
    }

    public record PriceQuote(List<PriceLine> Lines, int? FollowUpOfId)
    {
        public decimal Discount => Lines.Sum(l => l.Discount);

        /// <summary>" Kontrol qəbulu: 25,00 ₼ endirim." — appended to the booking message.</summary>
        public string Note => Discount > 0
            ? " " + string.Join(", ", Lines.Where(l => l.Discount > 0).GroupBy(l => l.Source).Select(g => $"{g.Key}: {Fmt.Money(g.Sum(x => x.Discount))} endirim")) + "."
            : "";
    }

    /// <summary>
    /// Prices for a booking. Read-only, so other modules (Emr ordering services) may ask too.
    /// Today: the follow-up rule (same doctor, within N days of a completed visit).
    /// </summary>
    public class PriceQueries
    {
        private readonly IAppDbContext _db;
        private readonly SchedulingOptions _options;

        public PriceQueries(IAppDbContext db, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public async Task<PriceQuote> QuoteAsync(int? patientId, int? doctorId, DateTime at, IEnumerable<ExaminationType> exams, int? excludeReserveId = null,
            CancellationToken ct = default)
        {
            var list = exams.ToList();
            var prior = await FollowUpOfAsync(patientId, doctorId, at, excludeReserveId, ct);
            var pct = Math.Clamp(_options.FollowUpDiscountPercent, 0, 100);
            var lines = list.Select(e =>
            {
                var discount = prior != null && pct > 0 && e.FollowUpDiscount ? Math.Round(e.Price * pct / 100m, 2) : 0m;
                return new PriceLine(e.Id, e.Price, discount, discount > 0 ? (pct == 100 ? "Kontrol qəbulu (pulsuz)" : $"Kontrol qəbulu (−{pct}%)") : null);
            }).ToList();
            return new PriceQuote(lines, prior?.Id);
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
