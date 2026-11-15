using ClinicReservation.Models;
using System.ComponentModel.DataAnnotations;

namespace ClinicReservation.ViewModels
{
    public class AccountViewModel
    {
        [Required(ErrorMessage = "İstifadəçi adı daxil edilməlidir")]
        public string UserName { get; set; } = "";

        [Required(ErrorMessage = "Şifrə daxil edilməlidir")]
        public string Password { get; set; } = "";

        /// <summary>Role chosen when an admin creates a new user.</summary>
        public string? RoleId { get; set; }

        public AppUser? User { get; set; }
    }

    public class UpdateUserViewModel
    {
        public string Id { get; set; } = "";
        public string? UserName { get; set; }

        [Required(ErrorMessage = "Rol seçilməlidir")]
        public string? RoleId { get; set; }
    }
}

namespace ClinicReservation.ViewModels
{
    public class ResetPasswordViewModel
    {
        public string Id { get; set; } = "";
        public string? UserName { get; set; }

        [Required(ErrorMessage = "Yeni şifrə daxil edilməlidir")]
        [MinLength(6, ErrorMessage = "Şifrə ən azı 6 simvol olmalıdır")]
        public string? NewPassword { get; set; }

        [Required(ErrorMessage = "Şifrəni təkrar daxil edin")]
        [Compare(nameof(NewPassword), ErrorMessage = "Şifrələr eyni deyil")]
        public string? ConfirmPassword { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Cari şifrə daxil edilməlidir")]
        public string? CurrentPassword { get; set; }

        [Required(ErrorMessage = "Yeni şifrə daxil edilməlidir")]
        [MinLength(6, ErrorMessage = "Şifrə ən azı 6 simvol olmalıdır")]
        public string? NewPassword { get; set; }

        [Required(ErrorMessage = "Şifrəni təkrar daxil edin")]
        [Compare(nameof(NewPassword), ErrorMessage = "Şifrələr eyni deyil")]
        public string? ConfirmPassword { get; set; }
    }
}
