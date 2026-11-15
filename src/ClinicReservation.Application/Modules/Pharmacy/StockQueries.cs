using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Pharmacy
{
    /// <summary>Read side of the pharmacy module: balances, expiry, item cards, documents, report, settings lists.</summary>
    public class StockQueries
    {
        private readonly IAppDbContext _db;

        public StockQueries(IAppDbContext db) => _db = db;

        public Task<List<Warehouse>> ActiveWarehousesAsync(CancellationToken ct = default) =>
            _db.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToListAsync(ct);

        public Task<List<Warehouse>> AllWarehousesAsync(CancellationToken ct = default) =>
            _db.Warehouses.OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToListAsync(ct);

        public Task<int?> MainWarehouseIdAsync(CancellationToken ct = default) =>
            _db.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsMain).ThenBy(w => w.Id).Select(w => (int?)w.Id).FirstOrDefaultAsync(ct);

        /// <summary>Usable (not expired) quantity per item in one warehouse.</summary>
        public async Task<Dictionary<int, decimal>> OnHandAsync(int warehouseId, CancellationToken ct = default)
        {
            var today = ClinicTime.Today;
            return await _db.StockBatches
                .Where(b => b.WarehouseId == warehouseId && b.Quantity > 0 && (b.ExpiryDate == null || b.ExpiryDate >= today))
                .GroupBy(b => b.StockItemId).Select(g => new { g.Key, Qty = g.Sum(b => b.Quantity) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        }

        // filter: all | low (az qalan və bitmiş) | expiry (vaxtı keçmiş və ya bitən)
        public async Task<StockIndexViewModel> BalancesAsync(int? warehouseId, StockItemKind? kind, string? q, string? filter, bool showCosts, CancellationToken ct = default)
        {
            var today = ClinicTime.Today;
            var soon = today.AddDays(StockService.ExpirySoonDays);
            q = q?.Trim();

            var lots = _db.StockBatches.Where(b => b.Quantity > 0);
            if (warehouseId != null) lots = lots.Where(b => b.WarehouseId == warehouseId);
            var byItem = await lots.GroupBy(b => b.StockItemId).Select(g => new
            {
                g.Key,
                Qty = g.Sum(b => b.Quantity),
                Expired = g.Sum(b => b.ExpiryDate != null && b.ExpiryDate < today ? b.Quantity : 0),
                Nearest = g.Min(b => b.ExpiryDate != null && b.ExpiryDate >= today ? b.ExpiryDate : null),
                Value = g.Sum(b => b.Quantity * b.UnitCost)
            }).ToDictionaryAsync(x => x.Key, ct);

            var stocked = byItem.Keys.ToList();
            var az = StringComparer.Create(Fmt.Az, true);
            var items = (await _db.StockItems.Where(i => i.IsActive || stocked.Contains(i.Id)).ToListAsync(ct))
                .OrderBy(i => i.Name, az).ThenBy(i => i.Form ?? "", az).ToList();
            var rows = items.Select(i =>
            {
                var b = byItem.GetValueOrDefault(i.Id);
                var row = new StockBalanceRow
                {
                    Item = i,
                    Quantity = b?.Qty ?? 0,
                    ExpiredQuantity = b?.Expired ?? 0,
                    NearestExpiry = b?.Nearest,
                    Value = b?.Value ?? 0
                };
                // Minimum applies to the whole clinic, so only judge it when all warehouses are shown.
                row.State = row.ExpiredQuantity > 0 ? StockState.Expired
                    : row.NearestExpiry != null && row.NearestExpiry <= soon ? StockState.Expiring
                    : row.Usable <= 0 && i.IsActive && warehouseId == null ? StockState.Out
                    : warehouseId == null && i.MinQuantity > 0 && row.Usable < i.MinQuantity ? StockState.Low
                    : StockState.Ok;
                return row;
            }).ToList();
            // One warehouse: only what is actually there.
            if (warehouseId != null) rows = rows.Where(r => r.Quantity > 0).ToList();

            var model = new StockIndexViewModel
            {
                Warehouses = await ActiveWarehousesAsync(ct),
                WarehouseId = warehouseId,
                Kind = kind,
                Query = q,
                Filter = filter is "low" or "expiry" ? filter : "all",
                ItemCount = rows.Count(r => r.Quantity > 0),
                LowCount = rows.Count(r => r.State is StockState.Low or StockState.Out),
                ExpiringCount = rows.Count(r => r.State == StockState.Expiring),
                ExpiredCount = rows.Count(r => r.State == StockState.Expired),
                TotalValue = rows.Sum(r => r.Value),
                ShowCosts = showCosts
            };

            IEnumerable<StockBalanceRow> shown = rows;
            if (kind != null) shown = shown.Where(r => r.Item.Kind == kind);
            if (!string.IsNullOrEmpty(q))
                shown = shown.Where(r => r.Item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (r.Item.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (r.Item.Form ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            shown = model.Filter switch
            {
                "low" => shown.Where(r => r.State is StockState.Low or StockState.Out),
                "expiry" => shown.Where(r => r.State is StockState.Expired or StockState.Expiring),
                // "Hamısı" = what is on the shelves; empty items live under "Az qalan" and in the catalog.
                _ => shown.Where(r => r.Quantity > 0)
            };
            model.Rows = shown.ToList();
            return model;
        }

        /// <summary>Lots that are expired or expire within <paramref name="days"/> days.</summary>
        public async Task<StockExpiryViewModel> ExpiryAsync(int? warehouseId, int days, bool showCosts, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 1, 730);
            var limit = ClinicTime.Today.AddDays(days);
            var q = _db.StockBatches.Include(b => b.StockItem).Include(b => b.Warehouse)
                .Where(b => b.Quantity > 0 && b.ExpiryDate != null && b.ExpiryDate <= limit);
            if (warehouseId != null) q = q.Where(b => b.WarehouseId == warehouseId);
            return new StockExpiryViewModel
            {
                Lots = await q.OrderBy(b => b.ExpiryDate).ThenBy(b => b.StockItem.Name).ToListAsync(ct),
                Warehouses = await ActiveWarehousesAsync(ct),
                WarehouseId = warehouseId,
                Days = days,
                ShowCosts = showCosts
            };
        }

        public async Task<StockItemDetailsViewModel?> ItemCardAsync(int id, bool showCosts, CancellationToken ct = default)
        {
            var item = await _db.StockItems.FindAsync(new object[] { id }, ct);
            if (item == null) return null;
            return new StockItemDetailsViewModel
            {
                Item = item,
                Lots = await _db.StockBatches.Include(b => b.Warehouse).Where(b => b.StockItemId == id && b.Quantity > 0)
                    .OrderBy(b => b.Warehouse.Name).ThenBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ToListAsync(ct),
                Movements = await _db.StockMovements.Include(m => m.StockDocument).ThenInclude(d => d.Patient)
                    .Include(m => m.Warehouse).Include(m => m.StockBatch)
                    .Where(m => m.StockItemId == id).OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Take(100).ToListAsync(ct),
                UsedIn = await _db.ExaminationTypeStockItems.Where(x => x.StockItemId == id).Select(x => x.ExaminationType).OrderBy(e => e.Name).ToListAsync(ct),
                ShowCosts = showCosts
            };
        }

        /// <summary>Dövriyyə: opening, in, out and closing per item (transfers ignored when all warehouses are shown).</summary>
        public async Task<StockReportViewModel> ReportAsync(DateTime? from, DateTime? to, int? warehouseId, bool showCosts, CancellationToken ct = default)
        {
            var end = (to ?? ClinicTime.Today).Date;
            var start = (from ?? new DateTime(end.Year, end.Month, 1)).Date;
            if (start > end) (start, end) = (end, start);
            var until = end.AddDays(1);

            var q = _db.StockMovements.AsQueryable();
            q = warehouseId != null
                ? q.Where(m => m.WarehouseId == warehouseId)
                : q.Where(m => m.StockDocument.Type != StockDocType.Transfer);

            var opening = await q.Where(m => m.At < start).GroupBy(m => m.StockItemId)
                .Select(g => new { g.Key, Qty = g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
            var period = await q.Where(m => m.At >= start && m.At < until).GroupBy(m => m.StockItemId)
                .Select(g => new
                {
                    g.Key,
                    In = g.Sum(m => m.Quantity > 0 ? m.Quantity : 0),
                    Out = g.Sum(m => m.Quantity < 0 ? -m.Quantity : 0),
                    OutValue = g.Sum(m => m.Quantity < 0 ? -m.Quantity * m.UnitCost : (m.IsReversal ? -m.Quantity * m.UnitCost : 0))
                }).ToDictionaryAsync(x => x.Key, ct);

            var ids = opening.Keys.Union(period.Keys).ToList();
            var items = await _db.StockItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
            var rows = items.Select(i =>
                {
                    var open = opening.GetValueOrDefault(i.Id);
                    var p = period.GetValueOrDefault(i.Id);
                    var inQ = p?.In ?? 0;
                    var outQ = p?.Out ?? 0;
                    return new MovementRow(i, open, inQ, outQ, open + inQ - outQ, Math.Max(0, p?.OutValue ?? 0));
                })
                .Where(r => r.Opening != 0 || r.In != 0 || r.Out != 0)
                .OrderBy(r => r.Item.Name).ToList();

            return new StockReportViewModel
            {
                From = start,
                To = end,
                WarehouseId = warehouseId,
                Warehouses = await AllWarehousesAsync(ct),
                Rows = rows,
                ShowCosts = showCosts
            };
        }

        // ---------- Documents ----------

        public async Task<StockDocListViewModel> DocumentsAsync(StockDocType? type, int? warehouseId, DateTime? from, DateTime? to, bool showCosts, CancellationToken ct = default)
        {
            var end = (to ?? ClinicTime.Today).Date;
            var start = (from ?? end.AddDays(-30)).Date;
            if (start > end) (start, end) = (end, start);

            var q = _db.StockDocuments
                .Include(d => d.FromWarehouse).Include(d => d.ToWarehouse).Include(d => d.Supplier).Include(d => d.Patient)
                .Include(d => d.Movements)
                .Where(d => d.CreatedAt >= start && d.CreatedAt < end.AddDays(1));
            if (type != null) q = q.Where(d => d.Type == type);
            if (warehouseId != null) q = q.Where(d => d.FromWarehouseId == warehouseId || d.ToWarehouseId == warehouseId);

            return new StockDocListViewModel
            {
                Documents = await q.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Take(300).AsSplitQuery().ToListAsync(ct),
                Type = type,
                WarehouseId = warehouseId,
                From = start,
                To = end,
                Warehouses = await AllWarehousesAsync(ct),
                ShowCosts = showCosts
            };
        }

        public Task<StockDocument?> DocumentAsync(int id, CancellationToken ct = default) => _db.StockDocuments
            .Include(d => d.FromWarehouse).Include(d => d.ToWarehouse).Include(d => d.Supplier).Include(d => d.Patient)
            .Include(d => d.Reserve)
            .Include(d => d.Movements).ThenInclude(m => m.StockItem)
            .Include(d => d.Movements).ThenInclude(m => m.StockBatch)
            .Include(d => d.Movements).ThenInclude(m => m.Warehouse)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        /// <summary>Documents tied to one visit (materials card on the reservation page).</summary>
        public Task<List<StockDocument>> ForReserveAsync(int reserveId, CancellationToken ct = default) =>
            _db.StockDocuments.Include(d => d.Movements).ThenInclude(m => m.StockItem).Include(d => d.FromWarehouse)
                .Where(d => d.ReserveId == reserveId).OrderBy(d => d.CreatedAt).ToListAsync(ct);

        /// <summary>A completed visit whose service materials were never taken (e.g. stock failed at that moment).</summary>
        public Task<bool> HasPendingMaterialsAsync(int reserveId, CancellationToken ct = default) =>
            _db.ReserveExaminationTypes.AnyAsync(x => x.ReserveId == reserveId && x.StockConsumedAt == null && x.ExaminationType.Materials.Any(), ct);

        /// <summary>Fills the lists a document form needs (items, warehouses, suppliers, on-hand hints).</summary>
        public async Task<StockDocFormViewModel> PrepareFormAsync(StockDocFormViewModel m, CancellationToken ct = default)
        {
            var selected = m.Lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).ToList();
            m.Items = await _db.StockItems.Where(i => i.IsActive || selected.Contains(i.Id)).OrderBy(i => i.Name).ThenBy(i => i.Form).ToListAsync(ct);
            m.Warehouses = await ActiveWarehousesAsync(ct);
            m.Suppliers = await _db.Suppliers.Where(s => s.IsActive || s.Id == m.SupplierId).OrderBy(s => s.Name).ToListAsync(ct);
            if (m.Type != StockDocType.Receipt)
                foreach (var w in m.Warehouses)
                    m.OnHand[w.Id] = await OnHandAsync(w.Id, ct);
            if (m.ReserveId != null)
                m.Reserve = await _db.Reserves.Include(r => r.Patient).Include(r => r.Doctor).FirstOrDefaultAsync(r => r.Id == m.ReserveId, ct);
            if (m.Lines.Count == 0) m.Lines.Add(new StockLineInput());
            return m;
        }

        /// <summary>Default source for a visit's write-off: the doctor's department store (if any).</summary>
        public async Task<(bool Found, int? WarehouseId)> WarehouseForReserveAsync(int reserveId, CancellationToken ct = default)
        {
            var r = await _db.Reserves.Include(x => x.Doctor).FirstOrDefaultAsync(x => x.Id == reserveId, ct);
            if (r == null) return (false, null);
            var deptId = r.Doctor?.DepartmentId;
            var wh = deptId == null ? null : await _db.Warehouses
                .Where(w => w.IsActive && w.DepartmentId == deptId).Select(w => (int?)w.Id).FirstOrDefaultAsync(ct);
            return (true, wh);
        }

        public async Task<StockCountViewModel> PrepareCountAsync(StockCountViewModel m, CancellationToken ct = default)
        {
            m.Warehouses = await ActiveWarehousesAsync(ct);
            if (m.WarehouseId != null)
                m.Lots = await _db.StockBatches.Include(b => b.StockItem)
                    .Where(b => b.WarehouseId == m.WarehouseId && b.Quantity > 0)
                    .OrderBy(b => b.StockItem.Name).ThenBy(b => b.StockItem.Form).ThenBy(b => b.ExpiryDate).ToListAsync(ct);
            return m;
        }

        // ---------- Settings lists ----------

        public async Task<(List<StockItem> Items, Dictionary<int, int> UsedIn)> CatalogAsync(StockItemKind? kind, string? q, CancellationToken ct = default)
        {
            var items = _db.StockItems.AsQueryable();
            if (kind != null) items = items.Where(i => i.Kind == kind);
            if (!string.IsNullOrEmpty(q))
            {
                var term = q.ToLower();
                items = items.Where(i => i.Name.ToLower().Contains(term) || (i.Code != null && i.Code.ToLower().Contains(term)));
            }
            var usedIn = await _db.ExaminationTypeStockItems.GroupBy(x => x.StockItemId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return (await items.OrderBy(i => i.Kind).ThenBy(i => i.Name).ThenBy(i => i.Form).ToListAsync(ct), usedIn);
        }

        public async Task<StockItem?> ItemAsync(int id, CancellationToken ct = default) =>
            await _db.StockItems.FindAsync(new object[] { id }, ct);

        public async Task<(List<Warehouse> Warehouses, List<Department> Departments, Dictionary<int, int> Stocked)> WarehousesPageAsync(CancellationToken ct = default)
        {
            var departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct);
            var stocked = await _db.StockBatches.Where(b => b.Quantity > 0).GroupBy(b => b.WarehouseId)
                .Select(g => new { g.Key, Count = g.Select(b => b.StockItemId).Distinct().Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var warehouses = await _db.Warehouses.Include(w => w.Department).OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToListAsync(ct);
            return (warehouses, departments, stocked);
        }

        public Task<List<Supplier>> SuppliersAsync(CancellationToken ct = default) =>
            _db.Suppliers.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync(ct);
    }
}
