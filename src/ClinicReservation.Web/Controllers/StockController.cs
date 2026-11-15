using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Pharmacy;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Aptek və anbar: balances, expiry dates, item cards, movement report.</summary>
    [HasPermission(Permissions.StockView, Permissions.StockIssue, Permissions.StockManage)]
    public class StockController : Controller
    {
        private readonly StockQueries _queries;

        public StockController(StockQueries queries) => _queries = queries;

        private bool ShowCosts => User.CanAny(Permissions.StockManage, Permissions.ReportsView);

        public async Task<IActionResult> Index(int? warehouseId, StockItemKind? kind, string? q, string? filter, CancellationToken ct) =>
            View(await _queries.BalancesAsync(warehouseId, kind, q, filter, ShowCosts, ct));

        public async Task<IActionResult> Expiry(int? warehouseId, CancellationToken ct, int days = StockService.ExpirySoonDays) =>
            View(await _queries.ExpiryAsync(warehouseId, days, ShowCosts, ct));

        public async Task<IActionResult> Item(int id, CancellationToken ct)
        {
            var model = await _queries.ItemCardAsync(id, ShowCosts, ct);
            return model == null ? NotFound() : View(model);
        }

        public async Task<IActionResult> Report(DateTime? from, DateTime? to, int? warehouseId, CancellationToken ct) =>
            View(await _queries.ReportAsync(from, to, warehouseId, ShowCosts, ct));
    }
}
