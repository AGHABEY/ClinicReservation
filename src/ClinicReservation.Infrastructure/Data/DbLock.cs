using Npgsql;

namespace ClinicReservation.Data
{
    /// <summary>
    /// PostgreSQL advisory locks: lets several app instances (behind a load balancer) agree that
    /// only one of them runs a job at a time — migrations at startup, the SMS reminder round, …
    /// The lock lives on its own connection and is released when that connection closes,
    /// so a crashed instance never leaves it stuck.
    /// </summary>
    public static class DbLock
    {
        public const long Migrations = Services.LockKeys.Migrations;
        public const long SmsReminders = Services.LockKeys.SmsReminders;

        /// <summary>Runs <paramref name="action"/> only if no other instance holds the lock; false = skipped.</summary>
        public static async Task<bool> TryRunAsync(string connectionString, long key, Func<Task> action, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            if (!await ScalarAsync(conn, "SELECT pg_try_advisory_lock(@k)", key, ct))
                return false;
            try
            {
                await action();
                return true;
            }
            finally
            {
                await ScalarAsync(conn, "SELECT pg_advisory_unlock(@k)", key, CancellationToken.None);
            }
        }

        /// <summary>Waits for the lock, then runs <paramref name="action"/> (used for migrations: the others wait, then find nothing to do).</summary>
        public static async Task RunExclusiveAsync(string connectionString, long key, Func<Task> action, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", conn))
            {
                cmd.Parameters.AddWithValue("k", key);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            try
            {
                await action();
            }
            finally
            {
                await ScalarAsync(conn, "SELECT pg_advisory_unlock(@k)", key, CancellationToken.None);
            }
        }

        private static async Task<bool> ScalarAsync(NpgsqlConnection conn, string sql, long key, CancellationToken ct)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("k", key);
            return (bool)(await cmd.ExecuteScalarAsync(ct))!;
        }
    }
}
