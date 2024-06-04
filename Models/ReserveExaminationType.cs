using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicReservation.Models
{
    public class ReserveExaminationType : BaseModel
    {
        public int ReserveId { get; set; }
        public Reserve Reserve { get; set; }


        public int ExaminationTypeId { get; set; }
        public ExaminationType ExaminationType { get; set; }
    }
}