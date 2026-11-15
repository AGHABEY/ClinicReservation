using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Stock documents: receipt, transfer, write-off, inventory count, cancel.</summary>
    [HasPermission(Permissions.StockView, Permissions.StockIssue, Permissions.StockManage)]
    public class StockDocController : Controller
    {
        private readonly AppDbContext _context;
        private readonly StockService _stock;
        private readonly AuditService _audit;

        public StockDocController(AppDbContext context, StockService stock, AuditService audit)
        {
            _context = context;
            _stock = stock;
            _audit = audit;
        }

        private bool ShowCosts => User.CanAny(Permissions.StockManage, Permissions.ReportsView);

        public IActionResult Index(StockDocType? type, int? warehouseId, DateTime? from, DateTime? to)
        {
            var end = (to ?? ClinicTime.Today).Date;
            var start = (from ?? end.AddDays(-30)).Date;
            if (start > end) (start, end) = (end, start);

            var q = _context.StockDocuments
                .Include(d => d.FromWarehouse).Include(d => d.ToWarehouse).Include(d => d.Supplier).Include(d => d.Patient)
                .Include(d => d.Movements)
                .Where(d => d.CreatedAt >= start && d.CreatedAt < end.AddDays(1));
            if (type != null) q = q.Where(d => d.Type == type);
            if (warehouseId != null) q = q.Where(d => d.FromWarehouseId == warehouseId || d.ToWarehouseId == warehouseId);

            return View(new StockDocListViewModel
            {
                Documents = q.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Take(300).ToList(),
                Type = type,
                WarehouseId = warehouseId,
                From = start,
                To = end,
                Warehouses = _context.Warehouses.OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToList(),
                ShowCosts = ShowCosts
            });
        }

        public IActionResult Details(int id)
        {
            var doc = Load(id);
            if (doc == null) return NotFound();
            ViewBag.ShowCosts = ShowCosts;
            return View(doc);
        }

        public IActionResult Print(int id)
        {
            var doc = Load(id);
            if (doc == null) return NotFound();
            ViewBag.ShowCosts = ShowCosts;
            return View(doc);
        }

        // ---------- Receipt (mədaxil) ----------

        [HasPermission(Permissions.StockManage)]
        public IActionResult Receipt()
        {
            var model = new StockDocFormViewModel { Type = StockDocType.Receipt, WarehouseId = _stock.MainWarehouse()?.Id };
            model.Lines.Add(new StockLineInput());
            return View("Form", Prepare(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public IActionResult Receipt(StockDocFormViewModel model)
        {
            model.Type = StockDocType.Receipt;
            var lines = Filled(model);
            if (lines.Any(l => l.ItemId == null || l.Quantity == null))
                model.Errors.Add("Hər sətirdə məhsulu və miqdarı yazın");
            if (model.Date.Date > ClinicTime.Today) model.Errors.Add("Qaimə tarixi gələcəkdə ola bilməz");
            if (model.Errors.Any() || model.WarehouseId == null)
            {
                if (model.WarehouseId == null) model.Errors.Add("Anbarı seçin");
                return View("Form", Prepare(model));
            }

            var o = _stock.Receive(model.WarehouseId.Value, model.SupplierId, Clean(model.InvoiceNo), model.Date.Date, Clean(model.Note),
                lines.Select(l => new StockService.ReceiptLine(l.ItemId!.Value, l.Quantity!.Value, l.UnitCost ?? 0, Clean(l.BatchNo), l.ExpiryDate)).ToList(),
                User.Identity?.Name);
            return Done(o, model);
        }

        // ---------- Transfer (köçürmə) ----------

        [HasPermission(Permissions.StockManage)]
        public IActionResult Transfer(int? fromId, int? toId)
        {
            var model = new StockDocFormViewModel { Type = StockDocType.Transfer, WarehouseId = fromId ?? _stock.MainWarehouse()?.Id, ToWarehouseId = toId };
            model.Lines.Add(new StockLineInput());
            return View("Form", Prepare(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public IActionResult Transfer(StockDocFormViewModel model)
        {
            model.Type = StockDocType.Transfer;
            var lines = Filled(model);
            if (lines.Any(l => l.ItemId == null || l.Quantity == null))
                model.Errors.Add("Hər sətirdə məhsulu və miqdarı yazın");
            if (model.WarehouseId == null || model.ToWarehouseId == null)
                model.Errors.Add("Hər iki anbarı seçin");
            if (model.Errors.Any()) return View("Form", Prepare(model));

            var o = _stock.Transfer(model.WarehouseId!.Value, model.ToWarehouseId!.Value, Clean(model.Note),
                lines.Select(l => new StockService.OutLine(l.ItemId!.Value, l.Quantity!.Value)).ToList(), User.Identity?.Name);
            return Done(o, model);
        }

        // ---------- Write-off / issue to a patient (silinmə) ----------

        [HasPermission(Permissions.StockManage, Permissions.StockIssue)]
        public IActionResult WriteOff(int? reserveId, int? warehouseId, string? reason)
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
                var r = _context.Reserves.Include(x => x.Doctor).FirstOrDefault(x => x.Id == reserveId);
                if (r == null) return NotFound();
                // The doctor's department cupboard is the likely source.
                model.WarehouseId ??= _context.Warehouses
                    .Where(w => w.IsActive && w.DepartmentId != null && r.Doctor != null && w.DepartmentId == r.Doctor.DepartmentId)
                    .Select(w => (int?)w.Id).FirstOrDefault();
            }
            model.WarehouseId ??= _stock.MainWarehouse()?.Id;
            model.Lines.Add(new StockLineInput());
            return View("Form", Prepare(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage, Permissions.StockIssue)]
        public IActionResult WriteOff(StockDocFormViewModel model)
        {
            model.Type = StockDocType.WriteOff;
            var lines = Filled(model);
            if (lines.Any(l => l.ItemId == null || l.Quantity == null))
                model.Errors.Add("Hər sətirdə məhsulu və miqdarı yazın");
            if (model.WarehouseId == null) model.Errors.Add("Anbarı seçin");
            if (string.IsNullOrWhiteSpace(model.Reason) || !StockLabels.WriteOffReasons.Contains(model.Reason))
                model.Errors.Add("Silinmə səbəbini seçin");

            int? patientId = null;
            if (model.ReserveId != null)
            {
                patientId = _context.Reserves.Where(r => r.Id == model.ReserveId).Select(r => (int?)r.PatientId).FirstOrDefault();
                if (patientId == null) model.Errors.Add("Rezervasiya tapılmadı");
            }
            else if (!string.IsNullOrWhiteSpace(model.PatientPin))
            {
                var pin = Patient.NormalizePin(model.PatientPin);
                patientId = _context.Patients.Where(p => p.PinCode == pin).Select(p => (int?)p.Id).FirstOrDefault();
                if (patientId == null) model.Errors.Add($"{pin} FİN-li xəstə tapılmadı");
            }
            if (model.Reason == "Xəstəyə verildi" && patientId == null)
                model.Errors.Add("Xəstəyə verilən dərman üçün xəstənin FİN-ini yazın");
            if (model.Errors.Any()) return View("Form", Prepare(model));

            var o = _stock.WriteOff(model.WarehouseId!.Value, model.Reason!, Clean(model.Note), patientId, model.ReserveId,
                lines.Select(l => new StockService.OutLine(l.ItemId!.Value, l.Quantity!.Value)).ToList(), User.Identity?.Name);
            return Done(o, model);
        }

        // ---------- Inventory count ----------

        [HasPermission(Permissions.StockManage)]
        public IActionResult Count(int? warehouseId)
        {
            var model = new StockCountViewModel { WarehouseId = warehouseId ?? _stock.MainWarehouse()?.Id };
            return View(PrepareCount(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public IActionResult Count(StockCountViewModel model)
        {
            if (model.WarehouseId == null) return RedirectToAction(nameof(Count));
            // Only lines someone actually filled in count; empty means "not counted".
            var counted = model.Counted.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!.Value);
            if (counted.Count == 0)
            {
                model.Errors.Add("Heç bir sətir doldurulmayıb — sayılan miqdarları yazın");
                return View(PrepareCount(model));
            }
            var o = _stock.Count(model.WarehouseId.Value, Clean(model.Note), counted, User.Identity?.Name);
            if (!o.Ok)
            {
                model.Errors.AddRange(o.Errors);
                return View(PrepareCount(model));
            }
            var doc = o.Document!;
            _audit.Log("İnventarizasiya", AuditEntities.Stock, doc.Id, $"{doc.Number}: {counted.Count} sətir sayıldı, {doc.Movements.Count} fərq");
            TempData["Success"] = doc.Movements.Count == 0
                ? $"{doc.Number}: sayılan miqdarlar sistemlə üst-üstə düşür"
                : $"{doc.Number}: {doc.Movements.Count} sətirdə fərq qeydə alındı";
            return RedirectToAction(nameof(Details), new { id = doc.Id });
        }

        // ---------- Cancel ----------

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage)]
        public IActionResult Cancel(int id, string? reason)
        {
            var o = _stock.Cancel(id, reason ?? "", User.Identity?.Name);
            if (!o.Ok)
            {
                TempData["Error"] = string.Join(" ", o.Errors);
                return RedirectToAction(nameof(Details), new { id });
            }
            var doc = o.Document!;
            _audit.Log("Anbar sənədi ləğv edildi", AuditEntities.Stock, doc.Id, $"{doc.Number} ({StockLabels.Doc(doc.Type)}): {doc.CancelReason}");
            TempData["Success"] = $"{doc.Number} ləğv edildi, qalıqlar geri qaytarıldı";
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>Takes a completed visit's materials again (after a failure or a shortage was fixed).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.StockManage, Permissions.StockIssue)]
        public IActionResult ConsumeReserve(int reserveId)
        {
            var reserve = _context.Reserves.Include(r => r.Status).FirstOrDefault(r => r.Id == reserveId);
            if (reserve == null) return NotFound();
            if (reserve.Status?.Name != StatusNames.Completed)
            {
                TempData["Error"] = "Materiallar qəbul tamamlananda silinir";
                return RedirectToAction("Details", "Reservation", new { id = reserveId });
            }
            var o = _stock.ConsumeForReserve(reserveId, false, User.Identity?.Name);
            var warn = StockService.Warning(o);
            if (warn != null) TempData["Error"] = warn;
            else TempData["Success"] = o.Documents.Any() ? "Materiallar anbardan silindi" : "Silinəcək material yoxdur";
            return RedirectToAction("Details", "Reservation", new { id = reserveId });
        }

        // ------------------------------------------------------------------

        private IActionResult Done(StockService.Outcome o, StockDocFormViewModel model)
        {
            if (!o.Ok)
            {
                model.Errors.AddRange(o.Errors);
                return View("Form", Prepare(model));
            }
            var doc = Load(o.Document!.Id)!;
            var lines = doc.Lines.Where(m => doc.Type != StockDocType.Transfer || m.Quantity > 0).ToList();
            _audit.Log($"{StockLabels.Doc(doc.Type)}", AuditEntities.Stock, doc.Id,
                $"{doc.Number}: " + string.Join(", ", lines.GroupBy(m => m.StockItem.Name).Select(g => $"{g.Key} {StockService.Qty(Math.Abs(g.Sum(m => m.Quantity)))}"))
                + (doc.Patient != null ? $"; xəstə {doc.Patient.FullName}" : ""));
            TempData["Success"] = $"{doc.Number} — {StockLabels.Doc(doc.Type).ToLower(Fmt.Az)} qeydə alındı";
            return model.ReserveId != null
                ? RedirectToAction("Details", "Reservation", new { id = model.ReserveId })
                : RedirectToAction(nameof(Details), new { id = doc.Id });
        }

        private static List<StockLineInput> Filled(StockDocFormViewModel model)
        {
            model.Lines = model.Lines.Where(l => l.ItemId != null || l.Quantity != null || !string.IsNullOrWhiteSpace(l.BatchNo)).ToList();
            return model.Lines;
        }

        private StockDocFormViewModel Prepare(StockDocFormViewModel m)
        {
            var selected = m.Lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).ToList();
            m.Items = _context.StockItems.Where(i => i.IsActive || selected.Contains(i.Id)).OrderBy(i => i.Name).ThenBy(i => i.Form).ToList();
            m.Warehouses = _context.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToList();
            m.Suppliers = _context.Suppliers.Where(s => s.IsActive || s.Id == m.SupplierId).OrderBy(s => s.Name).ToList();
            if (m.Type != StockDocType.Receipt)
                foreach (var w in m.Warehouses)
                    m.OnHand[w.Id] = _stock.OnHand(w.Id, usableOnly: true);
            if (m.ReserveId != null)
                m.Reserve = _context.Reserves.Include(r => r.Patient).Include(r => r.Doctor).FirstOrDefault(r => r.Id == m.ReserveId);
            if (m.Lines.Count == 0) m.Lines.Add(new StockLineInput());
            return m;
        }

        private StockCountViewModel PrepareCount(StockCountViewModel m)
        {
            m.Warehouses = _context.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToList();
            if (m.WarehouseId != null)
                m.Lots = _context.StockBatches.Include(b => b.StockItem)
                    .Where(b => b.WarehouseId == m.WarehouseId && b.Quantity > 0)
                    .OrderBy(b => b.StockItem.Name).ThenBy(b => b.StockItem.Form).ThenBy(b => b.ExpiryDate).ToList();
            return m;
        }

        private StockDocument? Load(int id) => _context.StockDocuments
            .Include(d => d.FromWarehouse).Include(d => d.ToWarehouse).Include(d => d.Supplier).Include(d => d.Patient)
            .Include(d => d.Reserve)
            .Include(d => d.Movements).ThenInclude(m => m.StockItem)
            .Include(d => d.Movements).ThenInclude(m => m.StockBatch)
            .Include(d => d.Movements).ThenInclude(m => m.Warehouse)
            .AsSplitQuery()
            .FirstOrDefault(d => d.Id == id);

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
