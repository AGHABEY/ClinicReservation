using System.ComponentModel.DataAnnotations;

namespace ClinicReservation.ViewModels
{
    public class AdminRoleViewModel
    {
        [Required(ErrorMessage = "Rolun adı daxil edilməlidir")]
        [StringLength(50)]
        public string? Name { get; set; }
    }
}
