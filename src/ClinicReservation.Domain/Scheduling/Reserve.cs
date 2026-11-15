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
        /// <summary>When it was marked «Gəlmədi», and whether the system did it (not reception).</summary>
        public DateTime? NoShowAt { get; set; }
        public bool NoShowAuto { get; set; }
        /// <summary>A follow-up (kontrol) of this earlier visit with the same doctor.</summary>
        public int? FollowUpOfId { get; set; }

        /// <summary>Squeezed into a time that was already taken (araya salınıb).</summary>
        public bool Overbooked { get; set; }
        /// <summary>Free-text note on the cancellation (the reason itself is CancellationReason).</summary>
        public string? CancelReason { get; set; }
        public int? CancellationReasonId { get; set; }
        public CancellationReason? CancellationReason { get; set; }
        public CancelledBy? CancelledBy { get; set; }

        /// <summary>The course this visit belongs to, and its number in it (1..Count).</summary>
        public int? SeriesId { get; set; }
        public ReserveSeries? Series { get; set; }
        public int? SeriesNo { get; set; }

        /// <summary>How the booking was made.</summary>
        public ReserveSource Source { get; set; } = ReserveSource.Reception;

        /// <summary>Device booked for this visit (from its services).</summary>
        public int? EquipmentId { get; set; }
        public Equipment? Equipment { get; set; }

        // ----- Queue (assigned when the patient checks in) -----
        /// <summary>Running number for the day within the ticket prefix.</summary>
        public int? QueueNumber { get; set; }
        /// <summary>What the patient sees on the ticket and the hall screen, e.g. "A-017".</summary>
        public string? QueueTicket { get; set; }
        /// <summary>Goes ahead in the doctor's queue (65+ set at check-in by age; others by reception).</summary>
        public QueuePriority Priority { get; set; }
        /// <summary>Last time the doctor called this patient in (shown on the hall screen).</summary>
        public DateTime? CalledAt { get; set; }

        // ----- Reminders -----
        public DateTime? ReminderSentAt { get; set; }
        /// <summary>Random token for the patient's confirm/cancel link (no login needed).</summary>
        public string? PatientToken { get; set; }

        public MedicalRecord? MedicalRecord { get; set; }

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
