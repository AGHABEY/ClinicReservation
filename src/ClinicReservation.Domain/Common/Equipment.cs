namespace ClinicReservation.Models
{
    /// <summary>
    /// A device or resource a service needs (USM aparatı, EKQ, fizioterapiya kabineti). While it is
    /// booked for one visit, no other visit that needs it can take the same time.
    /// </summary>
    public class Equipment : BaseModel
    {
        public string Name { get; set; } = "";
        /// <summary>Where it stands (informational).</summary>
        public int? RoomId { get; set; }
        public Room? Room { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Note { get; set; }
    }
}
