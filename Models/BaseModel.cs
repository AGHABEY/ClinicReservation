using System.ComponentModel.DataAnnotations;

namespace ClinicReservation.Models
{
    public class BaseModel
    {
        [Key]
        public int Id { get; set; }
    }
}
