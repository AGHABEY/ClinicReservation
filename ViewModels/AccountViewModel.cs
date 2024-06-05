using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
{
    public class AccountViewModel
    {
        public string UserName { get; set; }
        public string Password { get; set; }
        public AppUser? User { get; set; }
    }
}
