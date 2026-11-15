using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Departments (şöbələr) and rooms (kabinetlər) on one page.</summary>
    [HasPermission(Permissions.CatalogManage)]
    public class DepartmentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public DepartmentController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public IActionResult Index()
        {
            ViewBag.Departments = _context.Departments
                .OrderByDescending(d => d.IsActive).ThenBy(d => d.Name)
                .Select(d => new { Item = d, Doctors = d.Doctors.Count(x => x.IsActive) })
                .ToList()
                .Select(x => (x.Item, x.Doctors))
                .ToList();
            ViewBag.Rooms = _context.Rooms
                .OrderByDescending(r => r.IsActive).ThenBy(r => r.Name)
                .ToList()
                .Select(r => (r, _context.Doctors.Count(d => d.RoomId == r.Id && d.IsActive)))
                .ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveDepartment(int id, string? name, string? queuePrefix, bool isActive = true)
        {
            name = name?.Trim();
            queuePrefix = string.IsNullOrWhiteSpace(queuePrefix) ? null : queuePrefix.Trim().ToUpperInvariant();
            if (queuePrefix != null && !System.Text.RegularExpressions.Regex.IsMatch(queuePrefix, "^[A-Z]{1,3}$"))
            {
                TempData["Error"] = "Talon hərfi 1–3 latın hərfi olmalıdır (məs: K)";
                return RedirectToAction(nameof(Index));
            }
            if (string.IsNullOrEmpty(name))
            {
                TempData["Error"] = "Şöbənin adı boş ola bilməz";
                return RedirectToAction(nameof(Index));
            }
            if (_context.Departments.Any(d => d.Id != id && d.Name.ToLower() == name.ToLower()))
            {
                TempData["Error"] = $"«{name}» adlı şöbə artıq var";
                return RedirectToAction(nameof(Index));
            }

            var dep = id > 0 ? _context.Departments.Find(id) : new Department();
            if (dep == null) return NotFound();
            var isNew = id == 0;
            dep.Name = name;
            dep.QueuePrefix = queuePrefix;
            dep.IsActive = isNew || isActive;
            if (isNew) _context.Departments.Add(dep);
            _context.SaveChanges();
            _audit.Log(isNew ? "Şöbə əlavə edildi" : "Şöbə dəyişdirildi", AuditEntities.Catalog, dep.Id, $"{dep.Name}, talon {dep.QueuePrefix ?? QueueService.DefaultPrefix}{(dep.IsActive ? "" : " (deaktiv)")}");
            TempData["Success"] = isNew ? $"«{name}» şöbəsi əlavə edildi" : "Şöbə yeniləndi";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveRoom(int id, string? name, bool isActive = true)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                TempData["Error"] = "Kabinetin adı boş ola bilməz";
                return RedirectToAction(nameof(Index));
            }
            if (_context.Rooms.Any(r => r.Id != id && r.Name.ToLower() == name.ToLower()))
            {
                TempData["Error"] = $"«{name}» adlı kabinet artıq var";
                return RedirectToAction(nameof(Index));
            }

            var room = id > 0 ? _context.Rooms.Find(id) : new Room();
            if (room == null) return NotFound();
            var isNew = id == 0;
            room.Name = name;
            room.IsActive = isNew || isActive;
            if (isNew) _context.Rooms.Add(room);
            _context.SaveChanges();
            _audit.Log(isNew ? "Kabinet əlavə edildi" : "Kabinet dəyişdirildi", AuditEntities.Catalog, room.Id, $"{room.Name}{(room.IsActive ? "" : " (deaktiv)")}");
            TempData["Success"] = isNew ? $"«{name}» kabineti əlavə edildi" : "Kabinet yeniləndi";
            return RedirectToAction(nameof(Index));
        }
    }
}
