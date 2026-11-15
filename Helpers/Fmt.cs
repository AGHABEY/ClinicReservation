using System.Globalization;

namespace ClinicReservation.Helpers
{
    /// <summary>
    /// Display formatting in Azerbaijani conventions (e.g. "1 250,00 ₼",
    /// "15 noyabr 2026"), independent of the server's OS culture.
    /// Only used for output — model binding keeps the invariant culture so
    /// that &lt;input type="number"&gt; values like "12.50" parse correctly.
    /// </summary>
    public static class Fmt
    {
        public static readonly CultureInfo Az = LoadAz();

        private static CultureInfo LoadAz()
        {
            try { return CultureInfo.GetCultureInfo("az-Latn-AZ"); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }

        public static string Money(decimal value) => value.ToString("#,0.00", Az) + " ₼";
        public static string Date(DateTime value) => value.ToString("dd.MM.yyyy", Az);
        public static string Date(DateTime? value) => value.HasValue ? Date(value.Value) : "—";
        public static string DateTime(DateTime value) => value.ToString("dd.MM.yyyy HH:mm", Az);
        public static string DateTime(DateTime? value) => value.HasValue ? DateTime(value.Value) : "—";
        public static string Long(DateTime value) => value.ToString("d MMMM yyyy, dddd", Az);
        public static string DayShort(DateTime value) => value.ToString("dd.MM ddd", Az);

        /// <summary>Relative day label: "Bu gün", "Sabah", "Dünən" or "18.11 çər".</summary>
        public static string Day(DateTime value)
        {
            var diff = (value.Date - System.DateTime.Today).Days;
            return diff switch
            {
                0 => "Bu gün",
                1 => "Sabah",
                -1 => "Dünən",
                _ => value.ToString("dd.MM.yyyy, ddd", Az)
            };
        }

        public static string Time(DateTime value) => value.ToString("HH:mm", Az);

        /// <summary>"Bu gün, 14:30" / "18.11.2026, çər, 10:00".</summary>
        public static string Appointment(DateTime value) => $"{Day(value)}, {Time(value)}";

        public static string DayName(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "Bazar ertəsi",
            DayOfWeek.Tuesday => "Çərşənbə axşamı",
            DayOfWeek.Wednesday => "Çərşənbə",
            DayOfWeek.Thursday => "Cümə axşamı",
            DayOfWeek.Friday => "Cümə",
            DayOfWeek.Saturday => "Şənbə",
            _ => "Bazar"
        };

        public static string DayShortName(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "B.e.",
            DayOfWeek.Tuesday => "Ç.a.",
            DayOfWeek.Wednesday => "Ç.",
            DayOfWeek.Thursday => "C.a.",
            DayOfWeek.Friday => "C.",
            DayOfWeek.Saturday => "Ş.",
            _ => "B."
        };

        /// <summary>"Leyla Məmmədova" → "Dr. Leyla Məmmədova"; names that already start with "Dr" stay as they are.</summary>
        public static string Doctor(string? name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "";
            return name.StartsWith("Dr", StringComparison.OrdinalIgnoreCase) ? name : "Dr. " + name;
        }

        public static string Initials(string? name)
        {
            var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Take(2).Select(p => char.ToUpper(p[0], Az)));
        }

        public static int Age(DateTime birthday)
        {
            var today = System.DateTime.Today;
            var age = today.Year - birthday.Year;
            if (birthday.Date > today.AddYears(-age)) age--;
            return age;
        }
    }
}
