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
    }
}
