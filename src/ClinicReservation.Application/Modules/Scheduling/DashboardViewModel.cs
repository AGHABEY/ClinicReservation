using ClinicReservation.Models;

namespace ClinicReservation.Modules.Scheduling
{
    public class DashboardViewModel
    {
        public int TodayCount { get; set; }
        public int WaitingRoomCount { get; set; }
        public int InProgressCount { get; set; }
        public int CompletedToday { get; set; }
        public int TomorrowCount { get; set; }
        public int WaitingReservations { get; set; }

        public decimal TotalIncome { get; set; }
        public decimal TodayIncome { get; set; }
        public decimal Outstanding { get; set; }

        /// <summary>Set when the signed-in user is linked to a doctor: the list shows only their patients.</summary>
        public Doctor? MyDoctor { get; set; }

        /// <summary>Today's non-cancelled appointments, in time order.</summary>
        public List<Reserve> TodayAppointments { get; set; } = new();
    }
}
