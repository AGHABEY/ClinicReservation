using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalReservations { get; set; }
        public int WaitingReservations { get; set; }
        public int ConfirmedReservations { get; set; }
        public int ExaminationTypeCount { get; set; }
        public int UserCount { get; set; }

        public List<Reserve> RecentReservations { get; set; } = new();
    }
}
