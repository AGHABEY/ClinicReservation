using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicReservation.Models
{
    public class Reserve : BaseModel
    {
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;

        /// <summary>Null only for reservations made before doctors existed.</summary>
        public int? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }

        public int? RoomId { get; set; }
        public Room? Room { get; set; }

        /// <summary>When the patient is expected to come in.</summary>
        public DateTime AppointmentAt { get; set; }

        /// <summary>Length booked (the doctor's slot length at booking time).</summary>
        public int DurationMinutes { get; set; } = 30;

        public DateTime? AddedDate { get; set; }
        public DateTime? ConfirmDate { get; set; }
        public DateTime? ArrivedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? CancelDate { get; set; }
        public string? CancelReason { get; set; }

        public List<ReserveExaminationType>? ReserveExaminationTypes { get; set; }

        public List<Payment>? Payments { get; set; }

        public int? StatusId { get; set; }
        public ReserveStatus? Status { get; set; }

        [NotMapped]
        public bool IsCancelled => Status?.Name == StatusNames.Cancelled;

        [NotMapped]
        public DateTime AppointmentEnd => AppointmentAt.AddMinutes(Math.Max(1, DurationMinutes));

        /// <summary>Sum of the examinations, using the prices locked in at booking time.</summary>
        [NotMapped]
        public decimal Total => ReserveExaminationTypes?.Sum(x => x.Price) ?? 0;

        /// <summary>What the patient owes: nothing once cancelled or marked as no-show.</summary>
        [NotMapped]
        public decimal AmountDue => StatusNames.IsClosedWithoutVisit(Status?.Name) ? 0 : Total;

        [NotMapped]
        public decimal Paid => Payments?.Sum(p => p.Amount) ?? 0;
    }
}
