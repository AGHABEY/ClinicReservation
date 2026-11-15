using System.ComponentModel.DataAnnotations;
using ClinicReservation.Models;

namespace ClinicReservation.Modules.Patients
{
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

    public class PatientRow
    {
        public Patient Patient { get; set; } = null!;
        public int Visits { get; set; }
        public DateTime? LastVisit { get; set; }
        public DateTime? NextVisit { get; set; }
    }
}
