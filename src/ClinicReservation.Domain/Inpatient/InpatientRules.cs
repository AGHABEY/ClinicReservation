using System.Globalization;
using ClinicReservation.Helpers;

namespace ClinicReservation.Models
{
    /// <summary>Pure inpatient rules: bed-day billing and ward/patient checks.</summary>
    public static class InpatientRules
    {
        public record BedDayLine(BedStay Stay, int Days, decimal Amount);

        /// <summary>
        /// Bed-days per stay: nights spent (a same-day stay counts as one day for the whole admission).
        /// </summary>
        public static (List<BedDayLine> Lines, int Days, decimal Total) BedDays(Admission a, DateTime now)
        {
            var lines = a.Stays.Where(s => s.To == null || s.To > s.From).OrderBy(s => s.From)
                .Select(s => { var d = Math.Max(0, ((s.To ?? now).Date - s.From.Date).Days); return new BedDayLine(s, d, d * s.DailyPrice); })
                .ToList();
            if (lines.Count > 0 && lines.Sum(l => l.Days) == 0)
                lines[0] = lines[0] with { Days = 1, Amount = lines[0].Stay.DailyPrice };
            return (lines, lines.Sum(l => l.Days), lines.Sum(l => l.Amount));
        }

        /// <summary>Warning (not an error) when a man is put into a women's ward or the other way round.</summary>
        public static string? GenderWarning(Bed bed, Patient patient) =>
            (bed.Ward.Gender, patient.Gender) switch
            {
                (WardGender.Male, Gender.Female) => $"{bed.Ward.Name} kişi palatasıdır",
                (WardGender.Female, Gender.Male) => $"{bed.Ward.Name} qadın palatasıdır",
                _ => null
            };

        /// <summary>"T 37,8°, AT 165/100, Nəbz 92, SpO₂ 96%".</summary>
        public static string Vitals(Observation o)
        {
            var parts = new List<string>();
            if (o.Temperature != null) parts.Add($"T {o.Temperature.Value.ToString("0.0", Fmt.Az)}°");
            if (o.BpSystolic != null) parts.Add($"AT {o.BpSystolic}/{o.BpDiastolic}");
            if (o.Pulse != null) parts.Add($"Nəbz {o.Pulse}");
            if (o.RespRate != null) parts.Add($"TS {o.RespRate}");
            if (o.SpO2 != null) parts.Add($"SpO₂ {o.SpO2}%");
            return string.Join(", ", parts);
        }

        /// <summary>"8:00; 20.00 ,14:00" → "08:00;14:00;20:00". Empty = as needed (null).</summary>
        public static string? ParseTimes(string? raw, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var list = new SortedSet<TimeSpan>();
            foreach (var part in raw.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TimeSpan.TryParseExact(part.Replace('.', ':'), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t)
                    || t >= TimeSpan.FromDays(1))
                {
                    error = $"«{part}» düzgün vaxt deyil (məs. 08:00)";
                    return null;
                }
                list.Add(t);
            }
            if (list.Count > 8) { error = "Gündə ən çox 8 qəbul vaxtı"; return null; }
            return string.Join(";", list.Select(t => t.ToString(@"hh\:mm")));
        }
    }
}
