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

        /// <summary>A visit with the same doctor within this many days after a completed one is a follow-up…</summary>
        public int FollowUpWindowDays { get; set; } = 14;

        /// <summary>…and its services marked «kontrol endirimi» get this discount (100 = free; 0 = none).</summary>
        public int FollowUpDiscountPercent { get; set; } = 100;

        /// <summary>The «kontrol vaxtı çatanlar» list shows plans due within this many days.</summary>
        public int FollowUpListAheadDays { get; set; } = 7;

        /// <summary>Ticket letters of the reception and cashier queues (kept apart from department letters).</summary>
        public string ReceptionPrefix { get; set; } = "R";
        public string CashierPrefix { get; set; } = "KS";

        /// <summary>The kiosk checks a patient in from this many minutes before the visit…</summary>
        public int KioskEarlyMinutes { get; set; } = 120;
        /// <summary>…until this many minutes after its start (later: go to reception).</summary>
        public int KioskLateMinutes { get; set; } = 30;
    }
}
