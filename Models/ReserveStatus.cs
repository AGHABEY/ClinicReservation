namespace ClinicReservation.Models
{
    public class ReserveStatus : BaseModel
    {
        public string Name { get; set; }

        public List<Reserve> Reserves { get; set; }

    }
}
