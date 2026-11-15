namespace ClinicReservation.Models
{
    /// <summary>
    /// One-time SMS code for online booking. Only a hash of the code is stored; the booking request waits in
    /// <see cref="Payload"/> until the code is confirmed (so nothing can be swapped between the two steps).
    /// </summary>
    public class OtpCode : BaseModel
    {
        /// <summary>Opaque id used in the URL of the code page.</summary>
        public Guid Key { get; set; } = Guid.NewGuid();
        public string Phone { get; set; } = "";
        public string CodeHash { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int Attempts { get; set; }
        public DateTime? UsedAt { get; set; }
        public string? Ip { get; set; }
        public string Payload { get; set; } = "";
    }

    /// <summary>The patient's score of a visit (1–5) and comment, from the SMS link.</summary>
    public class VisitRating : BaseModel
    {
        public int ReserveId { get; set; }
        public Reserve Reserve { get; set; } = null!;
        public int PatientId { get; set; }
        public int? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }
        public int Score { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
