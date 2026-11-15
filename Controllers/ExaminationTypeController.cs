using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Services;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.CatalogManage, Permissions.ReservationsView)]
    public class ExaminationTypeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public ExaminationTypeController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public IActionResult Index()
        {
            var exTypes = _context.ExaminationTypes.Include(e => e.Department).Include(e => e.LabTests).ThenInclude(l => l.LabTest)
                .Include(e => e.Materials).ThenInclude(m => m.StockItem)
                .OrderBy(e => e.Department == null ? "" : e.Department.Name).ThenBy(e => e.Name).ToList();
            return View(exTypes);
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Add()
        {
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
            PrepareLabTests(new List<int>());
            PrepareMaterials(new());
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Add(ExaminationType model, List<int>? labTestIds, List<MaterialInput>? materialRows)
        {
            ModelState.Remove(nameof(model.Department));
            ModelState.Remove(nameof(model.LabTests));
            ModelState.Remove(nameof(model.Materials));
            var wantedMaterials = ValidMaterials(materialRows);
            if (!ModelState.IsValid)
            {
                ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
                PrepareLabTests(labTestIds ?? new());
                PrepareMaterials(materialRows ?? new());
                return View(model);
            }

            model.Name = model.Name.Trim();
            model.LabTests = ValidLabTests(labTestIds).Select(id => new ExaminationTypeLabTest { LabTestId = id }).ToList();
            model.Materials = wantedMaterials.Select(kv => new ExaminationTypeStockItem { StockItemId = kv.Key, Quantity = kv.Value }).ToList();
            _context.ExaminationTypes.Add(model);
            _context.SaveChanges();
            _audit.Log("Müayinə əlavə edildi", AuditEntities.Catalog, model.Id, $"{model.Name} — {Helpers.Fmt.Money(model.Price)}");
            TempData["Success"] = $"«{model.Name}» əlavə edildi";
            return RedirectToAction(nameof(Index));
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Edit(int id)
        {
            var et = _context.ExaminationTypes.Include(e => e.LabTests).Include(e => e.Materials).FirstOrDefault(e => e.Id == id);
            if (et == null)
                return NotFound();
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive || d.Id == et.DepartmentId).OrderBy(d => d.Name).ToList();
            PrepareLabTests(et.LabTests.Select(l => l.LabTestId).ToList());
            PrepareMaterials(et.Materials.Select(m => new MaterialInput { StockItemId = m.StockItemId, Quantity = m.Quantity }).ToList());
            return View(et);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Edit(ExaminationType model, List<int>? labTestIds, List<MaterialInput>? materialRows)
        {
            var existing = _context.ExaminationTypes.Include(e => e.LabTests).Include(e => e.Materials).FirstOrDefault(e => e.Id == model.Id);
            if (existing == null)
                return NotFound();

            ModelState.Remove(nameof(model.Department));
            ModelState.Remove(nameof(model.LabTests));
            ModelState.Remove(nameof(model.Materials));
            var wantedMaterials = ValidMaterials(materialRows);
            if (!ModelState.IsValid)
            {
                ViewBag.Departments = _context.Departments.Where(d => d.IsActive || d.Id == existing.DepartmentId).OrderBy(d => d.Name).ToList();
                PrepareLabTests(labTestIds ?? new());
                PrepareMaterials(materialRows ?? new());
                return View(model);
            }

            var oldMaterials = existing.Materials.ToDictionary(m => m.StockItemId, m => m.Quantity);
            existing.Materials.RemoveAll(m => !wantedMaterials.ContainsKey(m.StockItemId));
            foreach (var (itemId, qty) in wantedMaterials)
            {
                var row = existing.Materials.FirstOrDefault(m => m.StockItemId == itemId);
                if (row == null) existing.Materials.Add(new ExaminationTypeStockItem { StockItemId = itemId, Quantity = qty });
                else row.Quantity = qty;
            }
            var materialsChanged = oldMaterials.Count != wantedMaterials.Count
                || oldMaterials.Any(kv => !wantedMaterials.TryGetValue(kv.Key, out var q) || q != kv.Value);

            var wanted = ValidLabTests(labTestIds);
            var oldLab = existing.LabTests.Select(l => l.LabTestId).ToHashSet();
            existing.LabTests.RemoveAll(l => !wanted.Contains(l.LabTestId));
            foreach (var idToAdd in wanted.Where(w => !oldLab.Contains(w)))
                existing.LabTests.Add(new ExaminationTypeLabTest { LabTestId = idToAdd });
            var labChanged = !oldLab.SetEquals(wanted);

            var oldPrice = existing.Price;
            existing.Name = model.Name.Trim();
            existing.Description = model.Description;
            existing.Price = model.Price;
            existing.DepartmentId = model.DepartmentId;
            _context.SaveChanges();
            _audit.Log("Müayinə dəyişdirildi", AuditEntities.Catalog, existing.Id,
                existing.Name + (oldPrice != existing.Price ? $": qiymət {Helpers.Fmt.Money(oldPrice)} → {Helpers.Fmt.Money(existing.Price)}" : "")
                + (labChanged ? $"; analizlər: {wanted.Count}" : "")
                + (materialsChanged ? $"; materiallar: {wantedMaterials.Count}" : ""));
            TempData["Success"] = $"«{existing.Name}» yeniləndi";
            return RedirectToAction(nameof(Index));
        }

        private void PrepareLabTests(List<int> selected)
        {
            ViewBag.LabTests = _context.LabTests.Where(t => t.IsActive || selected.Contains(t.Id))
                .OrderBy(t => t.Category).ThenBy(t => t.SortOrder).ToList();
            ViewBag.SelectedLabTests = selected;
        }

        private void PrepareMaterials(List<MaterialInput> selected)
        {
            var ids = selected.Where(m => m.StockItemId != null).Select(m => m.StockItemId!.Value).ToList();
            ViewBag.StockItems = _context.StockItems.Where(i => i.IsActive || ids.Contains(i.Id))
                .OrderBy(i => i.Name).ThenBy(i => i.Form).ToList();
            ViewBag.Materials = selected.Where(m => m.StockItemId != null).ToList();
        }

        /// <summary>Item → quantity; unknown items and empty or non-positive quantities are dropped, duplicates added up.</summary>
        private Dictionary<int, decimal> ValidMaterials(List<MaterialInput>? rows)
        {
            var list = (rows ?? new()).Where(r => r.StockItemId != null && r.Quantity > 0).ToList();
            var ids = list.Select(r => r.StockItemId!.Value).Distinct().ToList();
            var known = _context.StockItems.Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToHashSet();
            if ((rows ?? new()).Any(r => r.StockItemId != null && !(r.Quantity > 0)))
                ModelState.AddModelError("", "Material sətirlərində miqdar 0-dan böyük olmalıdır");
            if (list.Any(r => r.Quantity > 10000))
                ModelState.AddModelError("", "Material miqdarı çox böyükdür");
            return list.Where(r => known.Contains(r.StockItemId!.Value))
                .GroupBy(r => r.StockItemId!.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity!.Value));
        }

        private HashSet<int> ValidLabTests(List<int>? ids)
        {
            var list = ids ?? new();
            return _context.LabTests.Where(t => list.Contains(t.Id)).Select(t => t.Id).ToHashSet();
        }
    }
}
