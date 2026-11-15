using System.ComponentModel.DataAnnotations;
using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
{
    public class RecordFormViewModel
    {
        public int ReserveId { get; set; }

        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Complaint { get; set; }

        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Anamnesis { get; set; }

        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Examination { get; set; }

        [StringLength(10, ErrorMessage = "Kod ən çox 10 simvol ola bilər")]
        [RegularExpression(@"^\s*[A-Za-z][0-9]{2}(\.[0-9A-Za-z]{1,4})?\s*$", ErrorMessage = "BXT-10 kodu düzgün deyil (məs: J06.9)")]
        public string? IcdCode { get; set; }

        [StringLength(300, ErrorMessage = "Diaqnoz ən çox 300 simvol ola bilər")]
        public string? Diagnosis { get; set; }

        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Treatment { get; set; }

        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Prescription { get; set; }

        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Recommendations { get; set; }

        [Range(1, 365, ErrorMessage = "1 ilə 365 gün arasında olmalıdır")]
        public int? FollowUpDays { get; set; }

        [StringLength(500, ErrorMessage = "Ən çox 500 simvol")]
        public string? Allergies { get; set; }

        [StringLength(500, ErrorMessage = "Ən çox 500 simvol")]
        public string? ChronicConditions { get; set; }

        /// <summary>"1" = save and complete the visit.</summary>
        public string? Finish { get; set; }

        // Display only
        public Reserve? Reserve { get; set; }
        public List<MedicalRecord> History { get; set; } = new();
        public bool Exists { get; set; }
    }
}
