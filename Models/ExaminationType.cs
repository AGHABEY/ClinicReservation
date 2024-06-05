using System.ComponentModel.DataAnnotations;

namespace ClinicReservation.Models
{
    public class ExaminationType : BaseModel
    {
        [Required]
        public string Name { get; set; }

        public string? Description { get; set; }
        [Required]
        public decimal Price { get; set; }
    }
}
