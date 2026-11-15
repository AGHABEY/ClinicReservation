using System.ComponentModel.DataAnnotations;
using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
{
    public class InpatientBoardViewModel
    {
        public List<(Bed Bed, Admission? Stay)> Beds { get; set; } = new();
        public List<Department> Departments { get; set; } = new();
        public int? DepartmentId { get; set; }
        public int AdmittedToday { get; set; }
        public int DischargedToday { get; set; }
        /// <summary>Active stays whose bed was removed from the board filter (or have no bed).</summary>
        public int ActiveTotal { get; set; }
    }

    public class InpatientListViewModel
    {
        public List<Admission> Admissions { get; set; } = new();
        /// <summary>active | discharged</summary>
        public string Status { get; set; } = "active";
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? Query { get; set; }
    }

    public class AdmitViewModel
    {
        public int? PatientId { get; set; }
        public string? PatientPin { get; set; }
        [Required(ErrorMessage = "Çarpayını seçin")]
        public int? BedId { get; set; }
        public int? DoctorId { get; set; }
        public int? ReserveId { get; set; }
        public AdmissionType Type { get; set; } = AdmissionType.Planned;

        [StringLength(10, ErrorMessage = "Kod ən çox 10 simvol ola bilər")]
        [RegularExpression(@"^\s*[A-Za-z][0-9]{2}(\.[0-9A-Za-z]{1,4})?\s*$", ErrorMessage = "BXT-10 kodu düzgün deyil (məs: I10)")]
        public string? IcdCode { get; set; }
        [Required(ErrorMessage = "Daxilolma diaqnozunu yazın")]
        [StringLength(300, ErrorMessage = "Ən çox 300 simvol")]
        public string? Diagnosis { get; set; }
        [StringLength(2000, ErrorMessage = "Ən çox 2000 simvol")]
        public string? Note { get; set; }

        // Display only
        public Patient? Patient { get; set; }
        public List<Bed> FreeBeds { get; set; } = new();
        public List<Doctor> Doctors { get; set; } = new();
    }

    public class ObservationInput
    {
        public int AdmissionId { get; set; }
        public ObservationKind Kind { get; set; }
        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Text { get; set; }
        [Range(30, 45, ErrorMessage = "Temperatur 30–45 °C arasında olmalıdır")]
        public decimal? Temperature { get; set; }
        [Range(20, 250, ErrorMessage = "Nəbz 20–250 arasında olmalıdır")]
        public int? Pulse { get; set; }
        [Range(50, 300, ErrorMessage = "Sistolik təzyiq 50–300 arasında olmalıdır")]
        public int? BpSystolic { get; set; }
        [Range(20, 200, ErrorMessage = "Diastolik təzyiq 20–200 arasında olmalıdır")]
        public int? BpDiastolic { get; set; }
        [Range(4, 80, ErrorMessage = "Tənəffüs sayı 4–80 arasında olmalıdır")]
        public int? RespRate { get; set; }
        [Range(50, 100, ErrorMessage = "SpO₂ 50–100 arasında olmalıdır")]
        public int? SpO2 { get; set; }
    }

    public class OrderInput
    {
        public int AdmissionId { get; set; }
        public int? StockItemId { get; set; }
        [StringLength(200, ErrorMessage = "Ən çox 200 simvol")]
        public string? DrugName { get; set; }
        [StringLength(50, ErrorMessage = "Ən çox 50 simvol")]
        public string? Dose { get; set; }
        [Range(0.001, 1000, ErrorMessage = "Miqdar 0-dan böyük olmalıdır")]
        public decimal QuantityPerDose { get; set; } = 1;
        public string? Route { get; set; }
        public string? Times { get; set; }
        /// <summary>How many days (empty = until stopped).</summary>
        [Range(1, 365, ErrorMessage = "1–365 gün")]
        public int? Days { get; set; }
        [StringLength(500, ErrorMessage = "Ən çox 500 simvol")]
        public string? Note { get; set; }
    }

    public class DischargeInput
    {
        public int AdmissionId { get; set; }
        [Required(ErrorMessage = "Nəticəni seçin")]
        public DischargeOutcome? Outcome { get; set; }
        [StringLength(10, ErrorMessage = "Kod ən çox 10 simvol ola bilər")]
        [RegularExpression(@"^\s*[A-Za-z][0-9]{2}(\.[0-9A-Za-z]{1,4})?\s*$", ErrorMessage = "BXT-10 kodu düzgün deyil (məs: I10)")]
        public string? IcdCode { get; set; }
        [Required(ErrorMessage = "Yekun diaqnozu yazın")]
        [StringLength(300, ErrorMessage = "Ən çox 300 simvol")]
        public string? Diagnosis { get; set; }
        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Course { get; set; }
        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Treatment { get; set; }
        [StringLength(4000, ErrorMessage = "Ən çox 4000 simvol")]
        public string? Recommendations { get; set; }
    }

    public class AdmissionViewModel
    {
        public Admission Admission { get; set; } = null!;
        /// <summary>journal | orders | mar | bill</summary>
        public string Tab { get; set; } = "journal";
        public DateTime Day { get; set; } = DateTime.Today;
        public List<Bed> FreeBeds { get; set; } = new();
        public List<StockItem> Medicines { get; set; } = new();
        public List<Admission> PreviousStays { get; set; } = new();
        public ObservationInput NewObservation { get; set; } = new();
        public OrderInput NewOrder { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public Dictionary<int, decimal> OnHand { get; set; } = new();
    }
}
