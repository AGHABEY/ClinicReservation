namespace ClinicReservation.Models
{
    public class Doctor : BaseModel
    {
        public string FullName { get; set; } = "";
        public string? Specialty { get; set; }
        public string? Phone { get; set; }

        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }

        /// <summary>Default room; reservations are booked into it.</summary>
        public int? RoomId { get; set; }
        public Room? Room { get; set; }

        /// <summary>Length of one appointment with this doctor.</summary>
        public int SlotMinutes { get; set; } = 30;

        /// <summary>Extra visits squeezed into busy times per day (needs the «araya salmaq» permission); 0 = none.</summary>
        public int MaxOverbookPerDay { get; set; } = 2;

        public bool IsActive { get; set; } = true;

        /// <summary>Optional login linked to this doctor (sees "my appointments").</summary>
        public string? UserId { get; set; }

        public List<DoctorWorkHours> WorkHours { get; set; } = new();
        public List<DoctorLeave> Leaves { get; set; } = new();
    }

    /// <summary>Weekly working hours: one row per working day.</summary>
    public class DoctorWorkHours : BaseModel
    {
        public int DoctorId { get; set; }
        public Doctor Doctor { get; set; } = null!;

        public DayOfWeek Day { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public TimeSpan? BreakStart { get; set; }
        public TimeSpan? BreakEnd { get; set; }
    }

    /// <summary>Məzuniyyət / day off: no slots between From and To (inclusive).</summary>
    public class DoctorLeave : BaseModel
    {
        public int DoctorId { get; set; }
        public Doctor Doctor { get; set; } = null!;

        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? Reason { get; set; }
    }
}
