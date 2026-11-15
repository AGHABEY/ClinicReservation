namespace ClinicReservation.Models
{
    /// <summary>Booking rules, appsettings.json → "Scheduling".</summary>
    public class SchedulingOptions
    {
        /// <summary>
        /// A visit still «Gözləyir / Təsdiqləndi» this many minutes after its end becomes «Gəlmədi»
        /// automatically. 0 = off (reception marks it by hand).
        /// </summary>
        public int AutoNoShowAfterMinutes { get; set; } = 60;

        /// <summary>Warn when booking a patient who missed this many visits (0 = never warn)…</summary>
        public int NoShowWarnAfter { get; set; } = 3;

        /// <summary>…within this many months.</summary>
        public int NoShowWindowMonths { get; set; } = 12;
    }
}
