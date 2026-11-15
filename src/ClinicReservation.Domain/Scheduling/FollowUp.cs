namespace ClinicReservation.Models
{
    public enum FollowUpStatus { Due = 1, Booked = 2, Closed = 3 }

    /// <summary>
    /// Kontrol qəbulu: the doctor asked to see the patient again N days after a visit. Created from the medical
    /// record (event), listed for reception as the date approaches, settled when the patient is booked again.
    /// </summary>
    public class FollowUp : BaseModel
    {
        /// <summary>The visit the doctor asked for the follow-up in (one plan per visit).</summary>
        public int FromReserveId { get; set; }
        public Reserve FromReserve { get; set; } = null!;
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public int? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }

        public DateTime DueDate { get; set; }
        public FollowUpStatus Status { get; set; } = FollowUpStatus.Due;
        public DateTime CreatedAt { get; set; }
        public DateTime? SmsSentAt { get; set; }

        public int? ReserveId { get; set; }
        public Reserve? Reserve { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CloseNote { get; set; }
    }
}
