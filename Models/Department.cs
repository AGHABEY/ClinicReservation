namespace ClinicReservation.Models
{
    /// <summary>Şöbə — e.g. Kardiologiya, Laboratoriya, Radiologiya.</summary>
    public class Department : BaseModel
    {
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;

        public List<Doctor> Doctors { get; set; } = new();
    }

    /// <summary>Kabinet / room or a shared device. Two doctors in the same room can't overlap.</summary>
    public class Room : BaseModel
    {
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }
}
