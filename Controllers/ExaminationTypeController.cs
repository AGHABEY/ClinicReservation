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
            var exTypes = _context.ExaminationTypes.Include(e => e.Department)
                .OrderBy(e => e.Department == null ? "" : e.Department.Name).ThenBy(e => e.Name).ToList();
            return View(exTypes);
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Add()
        {
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Add(ExaminationType model)
        {
            ModelState.Remove(nameof(model.Department));
            if (!ModelState.IsValid)
            {
                ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
                return View(model);
            }

            model.Name = model.Name.Trim();
            _context.ExaminationTypes.Add(model);
            _context.SaveChanges();
            _audit.Log("Müayinə əlavə edildi", AuditEntities.Catalog, model.Id, $"{model.Name} — {Helpers.Fmt.Money(model.Price)}");
            TempData["Success"] = $"«{model.Name}» əlavə edildi";
            return RedirectToAction(nameof(Index));
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Edit(int id)
        {
            var et = _context.ExaminationTypes.Find(id);
            if (et == null)
                return NotFound();
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive || d.Id == et.DepartmentId).OrderBy(d => d.Name).ToList();
            return View(et);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Edit(ExaminationType model)
        {
            var existing = _context.ExaminationTypes.Find(model.Id);
            if (existing == null)
                return NotFound();

            ModelState.Remove(nameof(model.Department));
            if (!ModelState.IsValid)
            {
                ViewBag.Departments = _context.Departments.Where(d => d.IsActive || d.Id == existing.DepartmentId).OrderBy(d => d.Name).ToList();
                return View(model);
            }

            var oldPrice = existing.Price;
            existing.Name = model.Name.Trim();
            existing.Description = model.Description;
            existing.Price = model.Price;
            existing.DepartmentId = model.DepartmentId;
            _context.SaveChanges();
            _audit.Log("Müayinə dəyişdirildi", AuditEntities.Catalog, existing.Id,
                existing.Name + (oldPrice != existing.Price ? $": qiymət {Helpers.Fmt.Money(oldPrice)} → {Helpers.Fmt.Money(existing.Price)}" : ""));
            TempData["Success"] = $"«{existing.Name}» yeniləndi";
            return RedirectToAction(nameof(Index));
        }
    }
}
