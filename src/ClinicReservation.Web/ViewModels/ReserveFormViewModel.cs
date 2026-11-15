using System.ComponentModel.DataAnnotations;
using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
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

        // ----- Display only -----
        public bool IsEdit => Id > 0;
        public bool HasPayments { get; set; }
        public bool CanReschedule { get; set; } = true;
        public Patient? ExistingPatient { get; set; }
    }

    public class CancelReserveViewModel
    {
        public int Id { get; set; }

        [StringLength(250)]
        public string? Reason { get; set; }
    }

    public class PatientFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ad daxil edilməlidir")]
        [StringLength(50, ErrorMessage = "Ad ən çox 50 simvol ola bilər")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Soyad daxil edilməlidir")]
        [StringLength(50, ErrorMessage = "Soyad ən çox 50 simvol ola bilər")]
        public string? Surname { get; set; }

        [Required(ErrorMessage = "FİN kod daxil edilməlidir")]
        [RegularExpression(@"^\s*[A-Za-z0-9]{7}\s*$", ErrorMessage = "FİN kod 7 simvol olmalıdır (hərf və rəqəm)")]
        public string? PinCode { get; set; }

        [Required(ErrorMessage = "Əlaqə nömrəsi daxil edilməlidir")]
        [RegularExpression(@"^\+?[0-9\s\-()]{9,20}$", ErrorMessage = "Telefon nömrəsi düzgün deyil (məs: 050 123 45 67)")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Doğum tarixi daxil edilməlidir")]
        public DateTime? Birthday { get; set; }

        [StringLength(500, ErrorMessage = "Qeyd ən çox 500 simvol ola bilər")]
        public string? Notes { get; set; }

        public Gender? Gender { get; set; }

        public bool IsEdit => Id > 0;
    }

    public class DoctorFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ad və soyad daxil edilməlidir")]
        [StringLength(100)]
        public string? FullName { get; set; }

        [StringLength(100)]
        public string? Specialty { get; set; }

        [RegularExpression(@"^\+?[0-9\s\-()]{9,20}$", ErrorMessage = "Telefon nömrəsi düzgün deyil")]
        public string? Phone { get; set; }

        public int? DepartmentId { get; set; }
        public int? RoomId { get; set; }

        [Range(5, 240, ErrorMessage = "Qəbul müddəti 5–240 dəqiqə olmalıdır")]
        public int SlotMinutes { get; set; } = 30;

        public bool IsActive { get; set; } = true;
        public string? UserId { get; set; }

        /// <summary>Seven entries, Monday first.</summary>
        public List<WorkDayInput> Days { get; set; } = new();

        public bool IsEdit => Id > 0;
    }

    public class WorkDayInput
    {
        public DayOfWeek Day { get; set; }
        public bool Works { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? BreakStart { get; set; }
        public string? BreakEnd { get; set; }
    }

    public class LeaveInput
    {
        public int DoctorId { get; set; }

        [Required(ErrorMessage = "Başlanğıc tarixi seçin")]
        public DateTime? From { get; set; }

        [Required(ErrorMessage = "Son tarixi seçin")]
        public DateTime? To { get; set; }

        [StringLength(100)]
        public string? Reason { get; set; }
    }
}
