using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Data
{
    /// <summary>IDistributedLock on PostgreSQL advisory locks (see DbLock).</summary>
    public sealed class PgDistributedLock : IDistributedLock
    {
        private readonly string _connectionString;

        public PgDistributedLock(AppDbContext context) =>
            _connectionString = context.Database.GetConnectionString()!;

        public Task<bool> TryRunAsync(long key, Func<Task> action, CancellationToken ct = default) =>
            DbLock.TryRunAsync(_connectionString, key, action, ct);
    }
}
