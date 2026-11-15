using System.Globalization;
using ClinicReservation.Helpers;
using ClinicReservation.Models;

// Namespace kept from before the split (views and services import ClinicReservation.Services).
namespace ClinicReservation.Services
{
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
