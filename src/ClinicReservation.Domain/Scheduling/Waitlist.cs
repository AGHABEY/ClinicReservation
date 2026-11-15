namespace ClinicReservation.Models
{
    public enum WaitlistStatus
    {
        /// <summary>Waiting for an earlier free time.</summary>
        Waiting = 1,
        /// <summary>A matching time was freed and offered (reception sees it; SMS if asked).</summary>
        Offered = 2,
        Booked = 3,
        Closed = 4
    }

    /// <summary>Gözləmə siyahısı: "if an earlier time frees up with this doctor / department, tell me".</summary>
    public class WaitlistEntry : BaseModel
    {
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        /// <summary>A specific doctor, or any doctor of the department.</summary>
        public int? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }
        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public int? ExaminationTypeId { get; set; }
        public ExaminationType? ExaminationType { get; set; }

        /// <summary>Days the patient can come (inclusive).</summary>
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        /// <summary>Optional time-of-day window, e.g. only mornings.</summary>
        public TimeSpan? TimeFrom { get; set; }
        public TimeSpan? TimeTo { get; set; }
        public bool NotifyBySms { get; set; } = true;
        public string? Note { get; set; }

        public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }

        public DateTime? OfferedAt { get; set; }
        /// <summary>The freed time that was offered, and with which doctor.</summary>
        public DateTime? OfferedSlot { get; set; }
        public int? OfferedDoctorId { get; set; }
        public Doctor? OfferedDoctor { get; set; }

        public int? ReserveId { get; set; }
        public Reserve? Reserve { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CloseNote { get; set; }

        /// <summary>A freed time fits this entry's days and hours.</summary>
        public bool Fits(DateTime at) =>
            at.Date >= FromDate.Date && at.Date <= ToDate.Date
            && (TimeFrom == null || at.TimeOfDay >= TimeFrom) && (TimeTo == null || at.TimeOfDay <= TimeTo);
    }
}
