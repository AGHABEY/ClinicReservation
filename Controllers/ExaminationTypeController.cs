using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Services;
using ClinicReservation.Models;
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
                .OrderBy(e => e.Department == null ? "" : e.Department.Name).ThenBy(e => e.Name).ToList();
            return View(exTypes);
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Add()
        {
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
            PrepareLabTests(new List<int>());
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Add(ExaminationType model, List<int>? labTestIds)
        {
            ModelState.Remove(nameof(model.Department));
            ModelState.Remove(nameof(model.LabTests));
            if (!ModelState.IsValid)
            {
                ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
                PrepareLabTests(labTestIds ?? new());
                return View(model);
            }

            model.Name = model.Name.Trim();
            model.LabTests = ValidLabTests(labTestIds).Select(id => new ExaminationTypeLabTest { LabTestId = id }).ToList();
            _context.ExaminationTypes.Add(model);
            _context.SaveChanges();
            _audit.Log("Müayinə əlavə edildi", AuditEntities.Catalog, model.Id, $"{model.Name} — {Helpers.Fmt.Money(model.Price)}");
            TempData["Success"] = $"«{model.Name}» əlavə edildi";
            return RedirectToAction(nameof(Index));
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Edit(int id)
        {
            var et = _context.ExaminationTypes.Include(e => e.LabTests).FirstOrDefault(e => e.Id == id);
            if (et == null)
                return NotFound();
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive || d.Id == et.DepartmentId).OrderBy(d => d.Name).ToList();
            PrepareLabTests(et.LabTests.Select(l => l.LabTestId).ToList());
            return View(et);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Edit(ExaminationType model, List<int>? labTestIds)
        {
            var existing = _context.ExaminationTypes.Include(e => e.LabTests).FirstOrDefault(e => e.Id == model.Id);
            if (existing == null)
                return NotFound();

            ModelState.Remove(nameof(model.Department));
            ModelState.Remove(nameof(model.LabTests));
            if (!ModelState.IsValid)
            {
                ViewBag.Departments = _context.Departments.Where(d => d.IsActive || d.Id == existing.DepartmentId).OrderBy(d => d.Name).ToList();
                PrepareLabTests(labTestIds ?? new());
                return View(model);
            }

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
                + (labChanged ? $"; analizlər: {wanted.Count}" : ""));
            TempData["Success"] = $"«{existing.Name}» yeniləndi";
            return RedirectToAction(nameof(Index));
        }

        private void PrepareLabTests(List<int> selected)
        {
            ViewBag.LabTests = _context.LabTests.Where(t => t.IsActive || selected.Contains(t.Id))
                .OrderBy(t => t.Category).ThenBy(t => t.SortOrder).ToList();
            ViewBag.SelectedLabTests = selected;
        }

        private HashSet<int> ValidLabTests(List<int>? ids)
        {
            var list = ids ?? new();
            return _context.LabTests.Where(t => list.Contains(t.Id)).Select(t => t.Id).ToHashSet();
        }
    }
}
