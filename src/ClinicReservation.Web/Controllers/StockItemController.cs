using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Web;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Stock settings: item catalog, warehouses and suppliers.</summary>
    [HasPermission(Permissions.StockManage)]
    public class StockItemController : Controller
    {
        private readonly StockQueries _queries;
        private readonly StockSettings _settings;

        public StockItemController(StockQueries queries, StockSettings settings)
        {
            _queries = queries;
            _settings = settings;
        }

        // ---------- Items ----------

        public async Task<IActionResult> Index(StockItemKind? kind, string? q, CancellationToken ct)
        {
            q = q?.Trim();
            var (items, usedIn) = await _queries.CatalogAsync(kind, q, ct);
            ViewBag.Kind = kind;
            ViewBag.Query = q;
            ViewBag.UsedIn = usedIn;
            return View(items);
        }

        public IActionResult Create() => View("Form", new StockItem());

        public async Task<IActionResult> Edit(int id, CancellationToken ct)
        {
            var item = await _queries.ItemAsync(id, ct);
            return item == null ? NotFound() : View("Form", item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(StockItem model, CancellationToken ct)
        {
            // Our own (Azerbaijani) checks in StockSettings replace the implicit "required" errors
            // (DisplayName is computed from Name and must not be validated on its own).
            foreach (var key in new[] { nameof(model.Name), nameof(model.Unit), nameof(model.Batches), nameof(model.DisplayName) })
                ModelState.Remove(key);
            if (!ModelState.IsValid) return View("Form", model);

            var r = await _settings.SaveItemAsync(model, ct);
            if (!r.Ok)
            {
                ModelState.AddErrors(r);
                return View("Form", model);
            }
            TempData["Success"] = r.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportDefaults(CancellationToken ct) =>
            Flash(await _settings.ImportDefaultsAsync(ct), nameof(Index));

        // ---------- Warehouses ----------

        public async Task<IActionResult> Warehouses(CancellationToken ct)
        {
            var (warehouses, departments, stocked) = await _queries.WarehousesPageAsync(ct);
            ViewBag.Departments = departments;
            ViewBag.Stocked = stocked;
            return View(warehouses);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveWarehouse(int id, string? name, int? departmentId, bool isMain, CancellationToken ct, bool isActive = true) =>
            Flash(await _settings.SaveWarehouseAsync(id, name, departmentId, isMain, isActive, ct), nameof(Warehouses));

        // ---------- Suppliers ----------

        public async Task<IActionResult> Suppliers(CancellationToken ct) => View(await _queries.SuppliersAsync(ct));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSupplier(int id, string? name, string? voen, string? phone, string? note, CancellationToken ct, bool isActive = true) =>
            Flash(await _settings.SaveSupplierAsync(id, name, voen, phone, note, isActive, ct), nameof(Suppliers));

        private IActionResult Flash(Result r, string action)
        {
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(action);
        }
    }
}
