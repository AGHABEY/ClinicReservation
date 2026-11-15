using ClinicReservation.Models;
using Microsoft.AspNetCore.Identity;

namespace ClinicReservation.ViewModels
{
    public class UserListViewModel
    {
        public List<AppUser> Users { get; set; }
        public List<IdentityRole> Roles { get; set; }
        public List<IdentityUserRole<string>> UserRoles { get; set; }
    }
}
