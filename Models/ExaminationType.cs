using System.ComponentModel.DataAnnotations;

namespace ClinicReservation.Models
{
    public class ExaminationType : BaseModel
    {
        [Required(ErrorMessage = "Ad daxil edilməlidir")]
        public string Name { get; set; } = "";

        public string? Description { get; set; }

        /// <summary>Optional: which department performs it (used for grouping and reports).</summary>
        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }

        [Required(ErrorMessage = "Qiymət daxil edilməlidir")]
        [Range(0.01, 1000000, ErrorMessage = "Qiymət 0-dan böyük olmalıdır")]
        public decimal Price { get; set; }

        /// <summary>Lab tests run for this examination (empty for non-lab services).</summary>
        public List<ExaminationTypeLabTest> LabTests { get; set; } = new();
    }
}
