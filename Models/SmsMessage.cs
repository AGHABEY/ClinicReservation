namespace ClinicReservation.Models
{
    public enum SmsStatus
    {
        /// <summary>Accepted by the SMS provider.</summary>
        Sent = 1,
        /// <summary>Test mode: written to the log only, nothing was sent.</summary>
        Simulated = 2,
        Failed = 3
    }

    /// <summary>Every SMS the clinic sends (or tried to send).</summary>
    public class SmsMessage : BaseModel
    {
        public DateTime CreatedAt { get; set; }

        public int? ReserveId { get; set; }
        public Reserve? Reserve { get; set; }

        public int? PatientId { get; set; }
        public Patient? Patient { get; set; }

        public string Phone { get; set; } = "";
        public string Text { get; set; } = "";

        /// <summary>"Xatırlatma" (automatic) or "Əl ilə" (sent by staff).</summary>
        public string Kind { get; set; } = "";

        public SmsStatus Status { get; set; }
        public string? Error { get; set; }

        /// <summary>Who sent it by hand (null for automatic reminders).</summary>
        public string? SentBy { get; set; }
    }
}
