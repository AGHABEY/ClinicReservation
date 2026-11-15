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
    }
}
