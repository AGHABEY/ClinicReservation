using System.Globalization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Services
{
    public class LabService
    {
        private readonly AppDbContext _context;

        public LabService(AppDbContext context) => _context = context;

        /// <summary>Lab tests the reservation's examinations require that have no sample yet.</summary>
        public List<LabTest> PendingTests(int reserveId)
        {
            var required = _context.ReserveExaminationTypes
                .Where(x => x.ReserveId == reserveId)
                .SelectMany(x => x.ExaminationType.LabTests.Select(l => l.LabTest))
                .Where(t => t.IsActive)
                .Distinct()
                .ToList();
            if (required.Count == 0) return required;

            var done = _context.LabResults
                .Where(r => r.LabOrder.ReserveId == reserveId && r.LabOrder.Status != LabOrderStatus.Cancelled)
                .Select(r => r.LabTestId)
                .ToHashSet();
            return required.Where(t => !done.Contains(t.Id)).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToList();
        }

        /// <summary>Creates one sample with a fresh barcode for the given tests.</summary>
        public LabOrder CreateOrder(Reserve reserve, IEnumerable<LabTest> tests, Gender? gender, string? userName, DateTime now)
        {
            var order = new LabOrder
            {
                ReserveId = reserve.Id,
                PatientId = reserve.PatientId,
                CollectedAt = now,
                CollectedBy = userName,
                Status = LabOrderStatus.Collected,
                Results = tests.Select(t => new LabResult
                {
                    LabTestId = t.Id,
                    Unit = t.Unit,
                    Reference = LabRef.Text(t, gender)
                }).ToList()
            };

            // Barcode = yyMMdd + running number; retry if two samples are taken at the same moment.
            var prefix = now.ToString("yyMMdd", CultureInfo.InvariantCulture);
            for (var attempt = 0; ; attempt++)
            {
                var last = _context.LabOrders.Where(o => o.Barcode.StartsWith(prefix))
                    .OrderByDescending(o => o.Barcode).Select(o => o.Barcode).FirstOrDefault();
                var next = last == null ? 1 : int.Parse(last[prefix.Length..], CultureInfo.InvariantCulture) + 1;
                order.Barcode = prefix + next.ToString("0000", CultureInfo.InvariantCulture);
                if (attempt == 0) _context.LabOrders.Add(order);
                try
                {
                    _context.SaveChanges();
                    return order;
                }
                catch (DbUpdateException) when (attempt < 3)
                {
                    // Barcode taken in the meantime — try the next number.
                }
            }
        }

        /// <summary>Latest validated value of each test for this patient before the given order.</summary>
        public Dictionary<int, LabResult> PreviousResults(int patientId, int beforeOrderId, IEnumerable<int> testIds)
        {
            var ids = testIds.ToList();
            return _context.LabResults
                .Include(r => r.LabOrder)
                .Where(r => r.LabOrder.PatientId == patientId && r.LabOrderId != beforeOrderId
                            && r.LabOrder.Status == LabOrderStatus.Validated && ids.Contains(r.LabTestId) && r.Value != null)
                .OrderByDescending(r => r.LabOrder.CollectedAt)
                .ToList()
                .GroupBy(r => r.LabTestId)
                .ToDictionary(g => g.Key, g => g.First());
        }
    }

    /// <summary>Reference ranges and flags.</summary>
    public static class LabRef
    {
        public static bool TryParseNumber(string? s, out decimal value) =>
            decimal.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

        /// <summary>Keeps the precision the value was entered with (1.010 → "1,010").</summary>
        public static string Num(decimal? v) => v?.ToString(Fmt.Az) ?? "";

        private static (decimal? Min, decimal? Max) Range(LabTest t, Gender? g) =>
            g == Gender.Female && (t.RefMinFemale != null || t.RefMaxFemale != null)
                ? (t.RefMinFemale, t.RefMaxFemale)
                : (t.RefMin, t.RefMax);

        private static bool HasFemaleRange(LabTest t) => t.RefMinFemale != null || t.RefMaxFemale != null;

        private static string RangeText(decimal? min, decimal? max) =>
            min != null && max != null ? $"{Num(min)} – {Num(max)}"
            : max != null ? $"< {Num(max)}"
            : min != null ? $"> {Num(min)}"
            : "";

        /// <summary>Reference shown on the report; both sexes when the patient's is unknown.</summary>
        public static string Text(LabTest t, Gender? g)
        {
            if (t.ResultType != LabResultType.Numeric)
                return t.RefText ?? "";
            if (!HasFemaleRange(t) || g != null)
            {
                var (min, max) = Range(t, g);
                return RangeText(min, max);
            }
            return $"K: {RangeText(t.RefMin, t.RefMax)} · Q: {RangeText(t.RefMinFemale, t.RefMaxFemale)}";
        }

        public static (LabFlag Flag, decimal? Number) Evaluate(LabTest t, Gender? g, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return (LabFlag.None, null);

            switch (t.ResultType)
            {
                case LabResultType.Numeric:
                    if (!TryParseNumber(value, out var n))
                        return (LabFlag.None, null);
                    if (t.CriticalMin != null && n < t.CriticalMin) return (LabFlag.CriticalLow, n);
                    if (t.CriticalMax != null && n > t.CriticalMax) return (LabFlag.CriticalHigh, n);

                    var (min, max) = EffectiveRange(t, g);
                    if (min != null && n < min) return (LabFlag.Low, n);
                    if (max != null && n > max) return (LabFlag.High, n);
                    return (min == null && max == null ? LabFlag.None : LabFlag.Normal, n);

                case LabResultType.Choice:
                    if (string.IsNullOrWhiteSpace(t.RefText)) return (LabFlag.None, null);
                    return (string.Equals(value.Trim(), t.RefText.Trim(), StringComparison.OrdinalIgnoreCase) ? LabFlag.Normal : LabFlag.Abnormal, null);

                default:
                    return (LabFlag.None, null);
            }
        }

        /// <summary>Range used for flags; with the sex unknown only values outside both ranges are flagged.</summary>
        public static (decimal? Min, decimal? Max) EffectiveRange(LabTest t, Gender? g) =>
            g == null && HasFemaleRange(t)
                ? (Min(t.RefMin, t.RefMinFemale), Max(t.RefMax, t.RefMaxFemale))
                : Range(t, g);

        private static decimal? Min(decimal? a, decimal? b) => a == null ? b : b == null ? a : Math.Min(a.Value, b.Value);
        private static decimal? Max(decimal? a, decimal? b) => a == null ? b : b == null ? a : Math.Max(a.Value, b.Value);

        public static string Arrow(LabFlag f) => f switch
        {
            LabFlag.Low => "↓",
            LabFlag.High => "↑",
            LabFlag.CriticalLow => "↓↓",
            LabFlag.CriticalHigh => "↑↑",
            LabFlag.Abnormal => "!",
            _ => ""
        };

        public static string Css(LabFlag f) => f switch
        {
            LabFlag.Low or LabFlag.High or LabFlag.Abnormal => "flag-abn",
            LabFlag.CriticalLow or LabFlag.CriticalHigh => "flag-crit",
            LabFlag.Normal => "flag-ok",
            _ => ""
        };
    }
}
