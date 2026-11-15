using System.ComponentModel.DataAnnotations;
using ClinicReservation.Models;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>Form used for both creating and editing a reservation.</summary>
    public class ReserveFormViewModel
    {
        public int Id { get; set; }

        // ----- Patient (looked up by FİN; a new card is created if none exists) -----
        public int? PatientId { get; set; }

        [Required(ErrorMessage = "FİN kod daxil edilməlidir")]
        [RegularExpression(@"^\s*[A-Za-z0-9]{7}\s*$", ErrorMessage = "FİN kod 7 simvol olmalıdır (hərf və rəqəm)")]
        public string? PinCode { get; set; }

        [Required(ErrorMessage = "Ad daxil edilməlidir")]
        [StringLength(50, ErrorMessage = "Ad ən çox 50 simvol ola bilər")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Soyad daxil edilməlidir")]
        [StringLength(50, ErrorMessage = "Soyad ən çox 50 simvol ola bilər")]
        public string? Surname { get; set; }

        [Required(ErrorMessage = "Əlaqə nömrəsi daxil edilməlidir")]
        [RegularExpression(@"^\+?[0-9\s\-()]{9,20}$", ErrorMessage = "Telefon nömrəsi düzgün deyil (məs: 050 123 45 67)")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Doğum tarixi daxil edilməlidir")]
        public DateTime? Birthday { get; set; }

        // ----- Appointment -----
        [Required(ErrorMessage = "Həkim seçilməlidir")]
        public int? DoctorId { get; set; }

        [Required(ErrorMessage = "Qəbul tarixi seçilməlidir")]
        public DateTime? AppointmentDate { get; set; }

        /// <summary>"HH:mm" of the chosen slot.</summary>
        [Required(ErrorMessage = "Qəbul saatı seçilməlidir")]
        public string? AppointmentTime { get; set; }

        public List<int>? ExaminationTypeIds { get; set; }

        /// <summary>Booked from this waitlist entry (it becomes «Rezerv edildi»).</summary>
        public int? WaitlistId { get; set; }

        /// <summary>How the booking reached the clinic.</summary>
        public ReserveSource Source { get; set; } = ReserveSource.Reception;

        // ----- Course (kurs): several sessions at the same time on chosen weekdays -----
        /// <summary>1 = a single visit.</summary>
        [Range(1, 60, ErrorMessage = "Seans sayı 1–60 olmalıdır")]
        public int SeriesCount { get; set; } = 1;
        /// <summary>Weekdays of the sessions (empty = the start date's weekday).</summary>
        public List<DayOfWeek>? SeriesDays { get; set; }
        /// <summary>"confirm" after the user has seen the planned dates.</summary>
        public string? Step { get; set; }
        /// <summary>Planned dates shown before confirming (display only).</summary>
        public List<SeriesPlanItem> SeriesPlan { get; set; } = new();

        // ----- Display only -----
        public bool IsEdit => Id > 0;
        public bool HasPayments { get; set; }
        public bool CanReschedule { get; set; } = true;
        public Patient? ExistingPatient { get; set; }
    }

    public class CancelReserveViewModel
    {
        public int Id { get; set; }

        /// <summary>Catalog reason (required when the catalog has any).</summary>
        public int? ReasonId { get; set; }
        public CancelledBy? By { get; set; }
        /// <summary>Also cancel the later sessions of the course.</summary>
        public bool Series { get; set; }

        [StringLength(250)]
        public string? Reason { get; set; }
    }

    /// <summary>One planned session: the asked time, a nearby free time that day, or nothing.</summary>
    public record SeriesPlanItem(DateTime Date, string? Time, SeriesPlanStatus Status, string? Note);

    public enum SeriesPlanStatus { Ok, Alternative, NoTime }

    /// <summary>Lists and slots the reservation form needs.</summary>
    public class ReserveFormData
    {
        public List<ExaminationType> ExaminationTypes { get; set; } = new();
        public List<Doctor> Doctors { get; set; } = new();
        public SlotResult Slots { get; set; } = new();
        /// <summary>Examination → price locked in on this reservation.</summary>
        public Dictionary<int, decimal> LockedPrices { get; set; } = new();
        /// <summary>Length and device of the chosen services.</summary>
        public SlotService.BookingShape Shape { get; set; } = new(0, null, null);
    }
}
