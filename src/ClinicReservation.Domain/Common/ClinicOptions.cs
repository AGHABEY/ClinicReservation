namespace ClinicReservation.Models
{
    /// <summary>
    /// Default working hours used to pre-fill a new doctor's weekly schedule.
    /// Configured in appsettings.json → "Clinic".
    /// </summary>
    public class ClinicOptions
    {
        public TimeSpan WorkStart { get; set; } = new(9, 0, 0);
        public TimeSpan WorkEnd { get; set; } = new(18, 0, 0);

        /// <summary>Length of one appointment slot in minutes.</summary>
        public int SlotMinutes { get; set; } = 30;

        /// <summary>
        /// The clinic's time zone (IANA id). Times are stored as local clinic time, so every server
        /// must run in this zone (TZ environment variable); a mismatch is logged at startup.
        /// </summary>
        public string TimeZone { get; set; } = "Asia/Baku";
    }
}
