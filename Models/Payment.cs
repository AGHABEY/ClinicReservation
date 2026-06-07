using System.ComponentModel.DataAnnotations;

namespace ClinicReservation.Models
{
    public class Payment : BaseModel
    {
        public int ReserveId { get; set; }
        public Reserve? Reserve { get; set; }

        [Required]
        public decimal Amount { get; set; }

        public PaymentMethod Method { get; set; }

        public DateTime PaidAt { get; set; }

        public string? Note { get; set; }
    }
}
