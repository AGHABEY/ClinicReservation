using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Stock settings: item catalog, warehouses and suppliers.</summary>
    [HasPermission(Permissions.StockManage)]
    public class StockItemController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public StockItemController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        // ---------- Items ----------

        public IActionResult Index(StockItemKind? kind, string? q)
        {
            q = q?.Trim();
            var items = _context.StockItems.AsQueryable();
            if (kind != null) items = items.Where(i => i.Kind == kind);
            if (!string.IsNullOrEmpty(q))
            {
                var like = $"%{q}%";
                items = items.Where(i => EF.Functions.ILike(i.Name, like) || (i.Code != null && EF.Functions.ILike(i.Code, like)));
            }
            ViewBag.Kind = kind;
            ViewBag.Query = q;
            ViewBag.UsedIn = _context.ExaminationTypeStockItems.GroupBy(x => x.StockItemId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
            return View(items.OrderBy(i => i.Kind).ThenBy(i => i.Name).ThenBy(i => i.Form).ToList());
        }

        public IActionResult Create() => View("Form", new StockItem());

        public IActionResult Edit(int id)
        {
            var item = _context.StockItems.Find(id);
            return item == null ? NotFound() : View("Form", item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Save(StockItem model)
        {
            foreach (var key in new[] { nameof(model.Name), nameof(model.Unit), nameof(model.Batches) })
                ModelState.Remove(key);
            model.Name = (model.Name ?? "").Trim();
            model.Code = Clean(model.Code)?.ToUpperInvariant();
            model.Unit = Clean(model.Unit) ?? "ədəd";

            if (model.Name.Length == 0) ModelState.AddModelError(nameof(model.Name), "Ad daxil edin");
            else if (model.Name.Length > 200) ModelState.AddModelError(nameof(model.Name), "Ad ən çox 200 simvol");
            if (model.Code is { Length: > 50 }) ModelState.AddModelError(nameof(model.Code), "Kod ən çox 50 simvol");
            else if (model.Code != null && _context.StockItems.Any(i => i.Code == model.Code && i.Id != model.Id))
                ModelState.AddModelError(nameof(model.Code), "Bu kodla məhsul artıq var");
            if (model.Unit.Length > 20) ModelState.AddModelError(nameof(model.Unit), "Vahid ən çox 20 simvol");
            if (model.MinQuantity < 0) ModelState.AddModelError(nameof(model.MinQuantity), "Mənfi ola bilməz");
            if (!Enum.IsDefined(model.Kind)) ModelState.AddModelError(nameof(model.Kind), "Növü seçin");

            if (!ModelState.IsValid) return View("Form", model);

            var isNew = model.Id == 0;
            var item = isNew ? new StockItem() : _context.StockItems.Find(model.Id);
            if (item == null) return NotFound();
            item.Name = model.Name;
            item.Code = model.Code;
            item.Kind = model.Kind;
            item.Form = Clean(model.Form);
            item.Unit = model.Unit;
            item.MinQuantity = model.MinQuantity;
            item.Note = Clean(model.Note);
            item.IsActive = model.IsActive;
            if (isNew) _context.StockItems.Add(item);
            _context.SaveChanges();

            _audit.Log(isNew ? "Məhsul kataloqa əlavə edildi" : "Məhsul dəyişdirildi", AuditEntities.Stock, item.Id,
                $"{item.DisplayName}, vahid {item.Unit}, minimum {StockService.Qty(item.MinQuantity)}");
            TempData["Success"] = isNew ? $"«{item.DisplayName}» əlavə edildi" : $"«{item.DisplayName}» yeniləndi";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ImportDefaults()
        {
            var existing = _context.StockItems.Where(i => i.Code != null).Select(i => i.Code!).ToHashSet();
            var added = StockCatalog.Defaults().Where(i => !existing.Contains(i.Code!)).ToList();
            _context.StockItems.AddRange(added);
            _context.SaveChanges();
            _audit.Log("Standart məhsullar əlavə edildi", AuditEntities.Stock, null, $"{added.Count} məhsul");
            TempData["Success"] = added.Count == 0
                ? "Bütün standart məhsullar artıq kataloqdadır"
                : $"{added.Count} məhsul əlavə edildi. Qalıq yaratmaq üçün mədaxil sənədi ilə qəbul edin.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Warehouses ----------

        public IActionResult Warehouses()
        {
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
            ViewBag.Stocked = _context.StockBatches.Where(b => b.Quantity > 0).GroupBy(b => b.WarehouseId)
                .Select(g => new { g.Key, Count = g.Select(b => b.StockItemId).Distinct().Count() }).ToDictionary(x => x.Key, x => x.Count);
            return View(_context.Warehouses.Include(w => w.Department).OrderByDescending(w => w.IsMain).ThenBy(w => w.Name).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveWarehouse(int id, string? name, int? departmentId, bool isMain, bool isActive = true)
        {
            name = Clean(name);
            if (name == null || name.Length > 100)
            {
                TempData["Error"] = "Anbarın adını yazın (ən çox 100 simvol)";
                return RedirectToAction(nameof(Warehouses));
            }
            var isNew = id == 0;
            var wh = isNew ? new Warehouse() : _context.Warehouses.Find(id);
            if (wh == null) return NotFound();
            if (!isActive && _context.StockBatches.Any(b => b.WarehouseId == wh.Id && b.Quantity > 0))
            {
                TempData["Error"] = $"«{wh.Name}» anbarında qalıq var — əvvəlcə köçürün və ya silin";
                return RedirectToAction(nameof(Warehouses));
            }
            if (departmentId != null && _context.Warehouses.Any(w => w.DepartmentId == departmentId && w.Id != wh.Id && w.IsActive))
            {
                TempData["Error"] = "Bu şöbənin artıq anbarı var";
                return RedirectToAction(nameof(Warehouses));
            }
            if (wh.IsMain && !isMain)
            {
                TempData["Error"] = "Mərkəzi anbarı dəyişmək üçün başqa anbarı mərkəzi edin";
                return RedirectToAction(nameof(Warehouses));
            }

            wh.Name = name;
            wh.DepartmentId = departmentId;
            wh.IsActive = isActive || wh.IsMain || isMain;
            if (isMain && !wh.IsMain)
            {
                foreach (var other in _context.Warehouses.Where(w => w.IsMain)) other.IsMain = false;
                wh.IsMain = true;
            }
            if (isNew) _context.Warehouses.Add(wh);
            _context.SaveChanges();
            _audit.Log(isNew ? "Anbar əlavə edildi" : "Anbar dəyişdirildi", AuditEntities.Stock, wh.Id, wh.Name + (wh.IsMain ? " (mərkəzi)" : ""));
            TempData["Success"] = isNew ? $"«{wh.Name}» əlavə edildi" : $"«{wh.Name}» yeniləndi";
            return RedirectToAction(nameof(Warehouses));
        }

        // ---------- Suppliers ----------

        public IActionResult Suppliers() => View(_context.Suppliers.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToList());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSupplier(int id, string? name, string? voen, string? phone, string? note, bool isActive = true)
        {
            name = Clean(name);
            voen = Clean(voen);
            if (name == null || name.Length > 200)
            {
                TempData["Error"] = "Təchizatçının adını yazın (ən çox 200 simvol)";
                return RedirectToAction(nameof(Suppliers));
            }
            if (voen != null && (voen.Length != 10 || !voen.All(char.IsDigit)))
            {
                TempData["Error"] = "VÖEN 10 rəqəmdən ibarət olmalıdır";
                return RedirectToAction(nameof(Suppliers));
            }
            var isNew = id == 0;
            var s = isNew ? new Supplier() : _context.Suppliers.Find(id);
            if (s == null) return NotFound();
            s.Name = name;
            s.Voen = voen;
            s.Phone = Clean(phone) is { } p && p.Length <= 32 ? p : null;
            s.Note = Clean(note) is { } n && n.Length <= 500 ? n : null;
            s.IsActive = isActive;
            if (isNew) _context.Suppliers.Add(s);
            _context.SaveChanges();
            _audit.Log(isNew ? "Təchizatçı əlavə edildi" : "Təchizatçı dəyişdirildi", AuditEntities.Stock, s.Id, s.Name);
            TempData["Success"] = isNew ? $"«{s.Name}» əlavə edildi" : $"«{s.Name}» yeniləndi";
            return RedirectToAction(nameof(Suppliers));
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
