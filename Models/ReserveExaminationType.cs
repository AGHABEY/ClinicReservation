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
    }
}
