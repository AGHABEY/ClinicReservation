using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicReservation.Models
{
    public class AppUser : IdentityUser
    {
        public string? FullName { get; set; }

        [NotMapped]
        public string? RoleId { get; set; }
    }
}
