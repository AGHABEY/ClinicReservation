using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Aptek və anbar: balances, expiry dates, item cards, movement report.</summary>
    [HasPermission(Permissions.StockView, Permissions.StockIssue, Permissions.StockManage)]
    public class StockController : Controller
    {
        private readonly AppDbContext _context;
        private readonly StockService _stock;

        public StockController(AppDbContext context, StockService stock)
        {
            _context = context;
            _stock = stock;
        }

        private bool ShowCosts => User.CanAny(Permissions.StockManage, Permissions.ReportsView);

        // filter: all | low (az qalan və bitmiş) | expiry (vaxtı keçmiş və ya bitən)
        public IActionResult Index(int? warehouseId, StockItemKind? kind, string? q, string? filter)
        {
            var today = ClinicTime.Today;
            var soon = today.AddDays(StockService.ExpirySoonDays);
            q = q?.Trim();

            var lots = _context.StockBatches.Where(b => b.Quantity > 0);
            if (warehouseId != null) lots = lots.Where(b => b.WarehouseId == warehouseId);
            var byItem = lots.GroupBy(b => b.StockItemId).Select(g => new
            {
                g.Key,
                Qty = g.Sum(b => b.Quantity),
                Expired = g.Sum(b => b.ExpiryDate != null && b.ExpiryDate < today ? b.Quantity : 0),
                Nearest = g.Min(b => b.ExpiryDate != null && b.ExpiryDate >= today ? b.ExpiryDate : null),
                Value = g.Sum(b => b.Quantity * b.UnitCost)
            }).ToDictionary(x => x.Key);

            var stocked = byItem.Keys.ToList();
            var az = StringComparer.Create(Helpers.Fmt.Az, true);
            var items = _context.StockItems.Where(i => i.IsActive || stocked.Contains(i.Id)).ToList()
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
                Warehouses = _context.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToList(),
                WarehouseId = warehouseId,
                Kind = kind,
                Query = q,
                Filter = filter is "low" or "expiry" ? filter : "all",
                ItemCount = rows.Count(r => r.Quantity > 0),
                LowCount = rows.Count(r => r.State is StockState.Low or StockState.Out),
                ExpiringCount = rows.Count(r => r.State == StockState.Expiring),
                ExpiredCount = rows.Count(r => r.State == StockState.Expired),
                TotalValue = rows.Sum(r => r.Value),
                ShowCosts = ShowCosts
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
            return View(model);
        }

        /// <summary>Lots that are expired or expire within <paramref name="days"/> days.</summary>
        public IActionResult Expiry(int? warehouseId, int days = StockService.ExpirySoonDays)
        {
            days = Math.Clamp(days, 1, 730);
            var limit = ClinicTime.Today.AddDays(days);
            var q = _context.StockBatches.Include(b => b.StockItem).Include(b => b.Warehouse)
                .Where(b => b.Quantity > 0 && b.ExpiryDate != null && b.ExpiryDate <= limit);
            if (warehouseId != null) q = q.Where(b => b.WarehouseId == warehouseId);
            return View(new StockExpiryViewModel
            {
                Lots = q.OrderBy(b => b.ExpiryDate).ThenBy(b => b.StockItem.Name).ToList(),
                Warehouses = _context.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToList(),
                WarehouseId = warehouseId,
                Days = days,
                ShowCosts = ShowCosts
            });
        }

        public IActionResult Item(int id)
        {
            var item = _context.StockItems.Find(id);
            if (item == null) return NotFound();
            return View(new StockItemDetailsViewModel
            {
                Item = item,
                Lots = _context.StockBatches.Include(b => b.Warehouse).Where(b => b.StockItemId == id && b.Quantity > 0)
                    .OrderBy(b => b.Warehouse.Name).ThenBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ToList(),
                Movements = _context.StockMovements.Include(m => m.StockDocument).ThenInclude(d => d.Patient)
                    .Include(m => m.Warehouse).Include(m => m.StockBatch)
                    .Where(m => m.StockItemId == id).OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Take(100).ToList(),
                UsedIn = _context.ExaminationTypeStockItems.Where(x => x.StockItemId == id).Select(x => x.ExaminationType).OrderBy(e => e.Name).ToList(),
                ShowCosts = ShowCosts
            });
        }

        /// <summary>Dövriyyə hesabatı: opening, in, out, closing per item.</summary>
        public IActionResult Report(DateTime? from, DateTime? to, int? warehouseId)
        {
            var end = (to ?? ClinicTime.Today).Date;
            var start = (from ?? new DateTime(end.Year, end.Month, 1)).Date;
            if (start > end) (start, end) = (end, start);
            return View(new StockReportViewModel
            {
                From = start,
                To = end,
                WarehouseId = warehouseId,
                Warehouses = _context.Warehouses.OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToList(),
                Rows = _stock.MovementReport(start, end.AddDays(1), warehouseId),
                ShowCosts = ShowCosts
            });
        }
    }
}
