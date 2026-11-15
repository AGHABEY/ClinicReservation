using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Audit
{
    /// <summary>Audit jurnalı: filtered, paged list of who did what.</summary>
    public class AuditQueries
    {
        public const int PageSize = 50;
        private readonly IAppDbContext _db;
        public AuditQueries(IAppDbContext db) => _db = db;

        public async Task<(List<AuditLog> Rows, int Total, List<string> Entities)> SearchAsync(string? entity, string? entityId, string? q,
            DateTime? from, DateTime? to, int page, CancellationToken ct = default)
        {
            var query = _db.AuditLogs.AsQueryable();
            if (!string.IsNullOrEmpty(entity)) query = query.Where(a => a.EntityType == entity);
            if (!string.IsNullOrEmpty(entityId)) query = query.Where(a => a.EntityId == entityId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(a => (a.UserName ?? "").ToLower().Contains(term)
                    || a.Action.ToLower().Contains(term)
                    || (a.Details ?? "").ToLower().Contains(term));
            }
            if (from.HasValue) query = query.Where(a => a.At >= from.Value.Date);
            if (to.HasValue) query = query.Where(a => a.At < to.Value.Date.AddDays(1));

            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
                .Skip((Math.Max(1, page) - 1) * PageSize).Take(PageSize).ToListAsync(ct);
            var entities = await _db.AuditLogs.Where(a => a.EntityType != null).Select(a => a.EntityType!).Distinct().OrderBy(x => x).ToListAsync(ct);
            return (rows, total, entities);
        }
    }
}
