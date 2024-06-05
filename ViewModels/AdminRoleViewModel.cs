using Microsoft.AspNetCore.Identity;

namespace ClinicReservation.ViewModels
{
    public class AdminRoleViewModel
    {
        public IdentityRole Role { get; set; }
        public List<IdentityRole> Roles { get; set; }
    }
}
