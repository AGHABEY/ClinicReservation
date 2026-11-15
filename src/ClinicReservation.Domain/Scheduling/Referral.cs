namespace ClinicReservation.Models
{
    public enum ReferralStatus { Open = 1, Booked = 2, Cancelled = 3 }
    public enum ReferralPriority { Routine = 1, Urgent = 2 }

    /// <summary>
    /// Göndəriş: a doctor (or an outside clinic) sends the patient to another specialist / department / service.
    /// Reception books it; it counts as carried out when that visit is completed.
    /// </summary>
    public class Referral : BaseModel
    {
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;

        /// <summary>The visit it was written in (null = from outside the clinic, see <see cref="ExternalFrom"/>).</summary>
        public int? FromReserveId { get; set; }
        public Reserve? FromReserve { get; set; }
        public int? FromDoctorId { get; set; }
        public Doctor? FromDoctor { get; set; }
        public string? ExternalFrom { get; set; }

        public int? ToDepartmentId { get; set; }
        public Department? ToDepartment { get; set; }
        public int? ToDoctorId { get; set; }
        public Doctor? ToDoctor { get; set; }
        public int? ExaminationTypeId { get; set; }
        public ExaminationType? ExaminationType { get; set; }

        public ReferralPriority Priority { get; set; } = ReferralPriority.Routine;
        public string? IcdCode { get; set; }
        public string Reason { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime ValidUntil { get; set; }

        public ReferralStatus Status { get; set; } = ReferralStatus.Open;
        /// <summary>The booked visit (kept if it was later cancelled — the referral is open again then).</summary>
        public int? ReserveId { get; set; }
        public Reserve? Reserve { get; set; }
        public DateTime? BookedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }
    }

    /// <summary>Where a referral stands for reception (derived from its status, its visit and the date).</summary>
    public enum ReferralState { Waiting, Booked, Done, NotDone }

    public static class ReferralFlow
    {
        public static ReferralState State(Referral r, DateTime today)
        {
            if (r.Status == ReferralStatus.Cancelled) return ReferralState.NotDone;
            var visit = r.Status == ReferralStatus.Booked ? r.Reserve?.Status?.Name : null;
            if (visit == StatusNames.Completed) return ReferralState.Done;
            // Booked and still ahead / in progress.
            if (visit != null && !StatusNames.IsClosedWithoutVisit(visit)) return ReferralState.Booked;
            // Open, or its visit was cancelled / missed: waiting until it expires.
            return r.ValidUntil.Date >= today.Date ? ReferralState.Waiting : ReferralState.NotDone;
        }

        public static string Label(ReferralState s) => s switch
        {
            ReferralState.Waiting => "Gözləyir",
            ReferralState.Booked => "Rezerv edilib",
            ReferralState.Done => "İcra olunub",
            _ => "İcra olunmayıb"
        };

        public static string Css(ReferralState s) => s switch
        {
            ReferralState.Waiting => "badge-waiting",
            ReferralState.Booked => "badge-confirmed",
            ReferralState.Done => "badge-completed",
            _ => "badge-noshow"
        };

        /// <summary>"Kardiologiya · Dr. X · EKQ" — whatever was chosen.</summary>
        public static string Target(Referral r) =>
            string.Join(" · ", new[] { r.ToDepartment?.Name, r.ToDoctor?.FullName, r.ExaminationType?.Name }.Where(x => !string.IsNullOrEmpty(x)));
    }
}
