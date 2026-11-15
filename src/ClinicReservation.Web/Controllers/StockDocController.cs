using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Pharmacy;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Stock documents: receipt, transfer, write-off, inventory count, cancel.</summary>
    [HasPermission(Permissions.StockView, Permissions.StockIssue, Permissions.StockManage)]
    public class StockDocController : Controller
    {
        private readonly StockQueries _queries;
        private readonly StockDocuments _documents;

        public StockDocController(StockQueries queries, StockDocuments documents)
        {
            _queries = queries;
            _documents = documents;
        }

        private bool ShowCosts => User.CanAny(Permissions.StockManage, Permissions.ReportsView);

        public async Task<IActionResult> Index(StockDocType? type, int? warehouseId, DateTime? from, DateTime? to, CancellationToken ct) =>
            View(await _queries.DocumentsAsync(type, warehouseId, from, to, ShowCosts, ct));

        public async Task<IActionResult> Details(int id, CancellationToken ct) => await DocumentView(id, ct);

        public async Task<IActionResult> Print(int id, CancellationToken ct) => await DocumentView(id, ct);

        private async Task<IActionResult> DocumentView(int id, CancellationToken ct)
        {
            var doc = await _queries.DocumentAsync(id, ct);
            if (doc == null) return NotFound();
            ViewBag.ShowCosts = ShowCosts;
            return View(doc);
        }

        // ---------- Receipt (mədaxil) ----------

        [HasPermission(Permissions.StockManage)]
        public async Task<IActionResult> Receipt(CancellationToken ct) =>
            await Form(new StockDocFormViewModel { Type = StockDocType.Receipt, WarehouseId = await _queries.MainWarehouseIdAsync(ct) }, ct);

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public async Task<IActionResult> Receipt(StockDocFormViewModel model, CancellationToken ct)
        {
            model.Type = StockDocType.Receipt;
            return await Posted(await _documents.ReceiveAsync(model, ct), model, ct);
        }

        // ---------- Transfer (köçürmə) ----------

        [HasPermission(Permissions.StockManage)]
        public async Task<IActionResult> Transfer(int? fromId, int? toId, CancellationToken ct) =>
            await Form(new StockDocFormViewModel { Type = StockDocType.Transfer, WarehouseId = fromId ?? await _queries.MainWarehouseIdAsync(ct), ToWarehouseId = toId }, ct);

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public async Task<IActionResult> Transfer(StockDocFormViewModel model, CancellationToken ct)
        {
            model.Type = StockDocType.Transfer;
            return await Posted(await _documents.TransferAsync(model, ct), model, ct);
        }

        // ---------- Write-off / issue to a patient (silinmə) ----------

        [HasPermission(Permissions.StockManage, Permissions.StockIssue)]
        public async Task<IActionResult> WriteOff(int? reserveId, int? warehouseId, string? reason, CancellationToken ct)
        {
            var model = new StockDocFormViewModel
            {
                Type = StockDocType.WriteOff,
                WarehouseId = warehouseId,
                ReserveId = reserveId,
                Reason = reason ?? (reserveId != null ? "Prosedurda istifadə edildi" : null)
            };
            if (reserveId != null)
            {
                // The doctor's department cupboard is the likely source.
                var (found, wh) = await _queries.WarehouseForReserveAsync(reserveId.Value, ct);
                if (!found) return NotFound();
                model.WarehouseId ??= wh;
            }
            model.WarehouseId ??= await _queries.MainWarehouseIdAsync(ct);
            return await Form(model, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage, Permissions.StockIssue)]
        public async Task<IActionResult> WriteOff(StockDocFormViewModel model, CancellationToken ct)
        {
            model.Type = StockDocType.WriteOff;
            return await Posted(await _documents.WriteOffAsync(model, ct), model, ct);
        }

        // ---------- Inventory count ----------

        [HasPermission(Permissions.StockManage)]
        public async Task<IActionResult> Count(int? warehouseId, CancellationToken ct) =>
            View(await _queries.PrepareCountAsync(new StockCountViewModel { WarehouseId = warehouseId ?? await _queries.MainWarehouseIdAsync(ct) }, ct));

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public async Task<IActionResult> Count(StockCountViewModel model, CancellationToken ct)
        {
            if (model.WarehouseId == null) return RedirectToAction(nameof(Count));
            var r = await _documents.CountAsync(model.WarehouseId.Value, model.Note, model.Counted, ct);
            if (!r.Ok)
            {
                model.Errors.AddRange(r.Errors.Select(e => e.Message));
                return View(await _queries.PrepareCountAsync(model, ct));
            }
            TempData["Success"] = r.Message;
            return RedirectToAction(nameof(Details), new { id = r.Id });
        }

        // ---------- Cancel ----------

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public IActionResult Cancel(int id, string? reason)
        {
            var r = _documents.Cancel(id, reason);
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>Takes a completed visit's materials again (after a failure or a shortage was fixed).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage, Permissions.StockIssue)]
        public async Task<IActionResult> ConsumeReserve(int reserveId, CancellationToken ct)
        {
            var r = await _documents.ConsumeForReserveAsync(reserveId, ct);
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction("Details", "Reservation", new { id = reserveId });
        }

        // ------------------------------------------------------------------

        private async Task<IActionResult> Form(StockDocFormViewModel model, CancellationToken ct) =>
            View("Form", await _queries.PrepareFormAsync(model, ct));

        private async Task<IActionResult> Posted(Result r, StockDocFormViewModel model, CancellationToken ct)
        {
            if (!r.Ok)
            {
                model.Errors.AddRange(r.Errors.Select(e => e.Message));
                return await Form(model, ct);
            }
            TempData["Success"] = r.Message;
            return model.ReserveId != null
                ? RedirectToAction("Details", "Reservation", new { id = model.ReserveId })
                : RedirectToAction(nameof(Details), new { id = r.Id });
        }
    }
}
