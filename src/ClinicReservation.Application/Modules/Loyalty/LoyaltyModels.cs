using System.ComponentModel.DataAnnotations;
using ClinicReservation.Models;

namespace ClinicReservation.Modules.Loyalty
{
    public class CampaignFormViewModel
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Adını yazın"), StringLength(100)]
        public string? Name { get; set; }
        /// <summary>Empty = automatic campaign; set = promo code.</summary>
        [StringLength(30), RegularExpression(@"^\s*[A-Za-z0-9\-]{3,30}\s*$", ErrorMessage = "Kod 3–30 hərf / rəqəm olmalıdır")]
        public string? Code { get; set; }
        public DiscountType Type { get; set; } = DiscountType.Percent;
        [Range(0.01, 100000, ErrorMessage = "Endirim 0-dan böyük olmalıdır")]
        public decimal Value { get; set; }
        [Required(ErrorMessage = "Başlanğıc tarixi")]
        public DateTime? StartsOn { get; set; }
        [Required(ErrorMessage = "Son tarix")]
        public DateTime? EndsOn { get; set; }
        public int? DepartmentId { get; set; }
        public List<int>? ExamIds { get; set; }
        [Range(1, 1000000, ErrorMessage = "Limit 1-dən böyük olmalıdır")]
        public int? MaxUses { get; set; }
        [Range(1, 1000, ErrorMessage = "Limit 1-dən böyük olmalıdır")]
        public int? MaxUsesPerPatient { get; set; }
        public bool NewPatientsOnly { get; set; }
        public bool IsActive { get; set; } = true;
        [StringLength(300)]
        public string? Note { get; set; }
    }
}
