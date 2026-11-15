namespace ClinicReservation.ViewModels
{
    /// <summary>One bookable time in a doctor's day (returned by SlotService).</summary>
    public class SlotViewModel
    {
        public string Time { get; set; } = "";
        public int Booked { get; set; }
        public int Capacity { get; set; }
        public bool Past { get; set; }
        public bool Available => !Past && Booked < Capacity;
    }
}
