namespace ClinicReservation.Services
{
    /// <summary>Clinic time for services that take it through DI (backed by ClinicTime).</summary>
    public interface IClock
    {
        DateTime Now { get; }
        DateTime Today { get; }
    }

    public sealed class ClinicClock : IClock
    {
        public DateTime Now => ClinicTime.Now;
        public DateTime Today => ClinicTime.Today;
    }
}
