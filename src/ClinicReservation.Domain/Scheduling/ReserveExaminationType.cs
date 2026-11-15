namespace ClinicReservation.Models
{
    public class ReserveExaminationType : BaseModel
    {
        public int ReserveId { get; set; }
        public Reserve Reserve { get; set; } = null!;

        public int ExaminationTypeId { get; set; }
        public ExaminationType ExaminationType { get; set; } = null!;

        /// <summary>
        /// Price of the examination at the moment the reservation was created.
        /// Stored separately so that later price changes do not rewrite the
        /// totals (and payment status) of existing reservations.
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>Catalog price at booking; <see cref="Price"/> = ListPrice − Discount (what the patient pays).</summary>
        public decimal ListPrice { get; set; }
        public decimal Discount { get; set; }
        /// <summary>Why it is cheaper (e.g. «Kontrol qəbulu», a campaign, a card); null = no discount.</summary>
        public string? DiscountSource { get; set; }

        /// <summary>When this line's materials were taken from stock (null = not yet).</summary>
        public DateTime? StockConsumedAt { get; set; }
    }
}
