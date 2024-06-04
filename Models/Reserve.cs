using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace ClinicReservation.Models
{
    public class Reserve : BaseModel
    {
        [Required]
        public string Name { get; set; }
        [Required]
        public string Surname { get; set; }
        [Required]
        public string PinCode { get; set; }
        [Required]
        public string Phone { get; set; }
        public DateTime Birthday { get; set; }
        public DateTime? AddedDate { get; set; }
        public DateTime? ConfirmDate { get; set; }

        public List<ReserveExaminationType>? ReserveExaminationTypes { get; set; }

        public int? StatusId { get; set; }
        public ReserveStatus? Status { get; set; }

        [NotMapped]
        public List<int> ReserveExaminationTypesId { get; set; }


    }
}
