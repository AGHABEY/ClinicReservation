namespace ClinicReservation
{
    /// <summary>
    /// The clinic's wall clock. Every appointment, dose and document time is stored as local
    /// clinic time, so "now" must come from the clinic's time zone — not from whatever zone the
    /// server happens to run in. Configured once at startup (Clinic:TimeZone); tests can pin it.
    /// Use this instead of DateTime.Now / DateTime.Today.
    /// </summary>
    public static class ClinicTime
    {
        private static TimeZoneInfo? _zone;
        private static Func<DateTime>? _fixed;

        public static DateTime Now => _fixed?.Invoke()
            ?? (_zone == null ? DateTime.Now : TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone));

        public static DateTime Today => Now.Date;

        public static TimeZoneInfo Zone => _zone ?? TimeZoneInfo.Local;

        public static void UseTimeZone(TimeZoneInfo zone) => _zone = zone;

        /// <summary>For tests: freeze or script the clock; null goes back to real time.</summary>
        public static void UseFixed(Func<DateTime>? now) => _fixed = now;
    }
}
