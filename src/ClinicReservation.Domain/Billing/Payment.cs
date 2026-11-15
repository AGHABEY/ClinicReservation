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

        /// <summary>Known before the insert, so the event about it is stored in the same save.</summary>
        public Guid Key { get; set; } = Guid.NewGuid();
    }
}
