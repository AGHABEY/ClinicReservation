namespace ClinicReservation.Services
{
    /// <summary>A lock shared by all app instances (so a job runs on one server at a time).</summary>
    public interface IDistributedLock
    {
        /// <summary>Runs <paramref name="action"/> if no other instance holds <paramref name="key"/>; false = skipped.</summary>
        Task<bool> TryRunAsync(long key, Func<Task> action, CancellationToken ct = default);
    }

    public static class LockKeys
    {
        public const long Migrations = 7_301_001;
        public const long SmsReminders = 7_301_002;
    }
}
