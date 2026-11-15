using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Palatalar və çarpayılar.</summary>
    [HasPermission(Permissions.InpatientManage)]
    public class WardController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public WardController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public IActionResult Index()
        {
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
            ViewBag.Occupied = _context.Admissions.Where(a => a.Status == AdmissionStatus.Active && a.BedId != null)
                .Select(a => a.BedId!.Value).ToHashSet();
            return View(_context.Wards.Include(w => w.Department).Include(w => w.Beds)
                .OrderBy(w => w.Department == null ? "" : w.Department.Name).ThenBy(w => w.SortOrder).ThenBy(w => w.Name).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Save(int id, string? name, int? departmentId, WardGender gender, decimal dailyPrice, int sortOrder,
            bool isActive = true, int bedCount = 0)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100) return Back("Palatanın adını yazın (ən çox 100 simvol)");
            if (dailyPrice < 0 || dailyPrice > 100000) return Back("Yataq-gün qiyməti düzgün deyil");
            if (!Enum.IsDefined(gender)) return Back("Palata növünü seçin");
            if (departmentId != null && !_context.Departments.Any(d => d.Id == departmentId)) return Back("Şöbə tapılmadı");
            var isNew = id == 0;
            if (isNew && (bedCount < 1 || bedCount > 30)) return Back("Çarpayı sayı 1–30 arasında olmalıdır");

            var ward = isNew ? new Ward() : _context.Wards.Find(id);
            if (ward == null) return NotFound();
            if (!isActive && ward.IsActive && _context.Admissions.Any(a => a.Status == AdmissionStatus.Active && a.Bed!.WardId == ward.Id))
                return Back($"«{ward.Name}» palatasında xəstə var — əvvəlcə köçürün");
            if (_context.Wards.Any(w => w.Id != ward.Id && w.Name == name && w.DepartmentId == departmentId))
                return Back("Bu şöbədə eyni adlı palata var");

            var oldPrice = ward.DailyPrice;
            ward.Name = name;
            ward.DepartmentId = departmentId;
            ward.Gender = gender;
            ward.DailyPrice = dailyPrice;
            ward.SortOrder = sortOrder;
            ward.IsActive = isActive;
            if (isNew)
            {
                for (var i = 1; i <= bedCount; i++) ward.Beds.Add(new Bed { Name = i.ToString() });
                _context.Wards.Add(ward);
            }
            _context.SaveChanges();
            _audit.Log(isNew ? "Palata əlavə edildi" : "Palata dəyişdirildi", AuditEntities.Inpatient, null,
                $"{ward.Name}, {InpatientLabels.Gender(ward.Gender)}, yataq-gün {Helpers.Fmt.Money(ward.DailyPrice)}"
                + (isNew ? $", {bedCount} çarpayı" : oldPrice != ward.DailyPrice ? $" (əvvəl {Helpers.Fmt.Money(oldPrice)})" : ""));
            TempData["Success"] = isNew ? $"«{ward.Name}» palatası {bedCount} çarpayı ilə əlavə edildi" : $"«{ward.Name}» yeniləndi";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddBed(int wardId, string? name)
        {
            var ward = _context.Wards.Include(w => w.Beds).FirstOrDefault(w => w.Id == wardId);
            if (ward == null) return NotFound();
            name = string.IsNullOrWhiteSpace(name) ? NextName(ward) : name.Trim();
            if (name.Length > 20) return Back("Çarpayı adı ən çox 20 simvol");
            if (ward.Beds.Any(b => b.Name == name)) return Back($"«{ward.Name}» palatasında {name} adlı çarpayı var");
            ward.Beds.Add(new Bed { Name = name });
            _context.SaveChanges();
            _audit.Log("Çarpayı əlavə edildi", AuditEntities.Inpatient, null, $"{ward.Name} · {name}");
            TempData["Success"] = $"{ward.Name}: {name} nömrəli çarpayı əlavə edildi";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Takes a bed out of use (repair) or back; an occupied bed can't be switched off.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleBed(int id)
        {
            var bed = _context.Beds.Include(b => b.Ward).FirstOrDefault(b => b.Id == id);
            if (bed == null) return NotFound();
            if (bed.IsActive && _context.Admissions.Any(a => a.BedId == id && a.Status == AdmissionStatus.Active))
                return Back($"{bed.Label} doludur — əvvəlcə xəstəni köçürün");
            bed.IsActive = !bed.IsActive;
            _context.SaveChanges();
            _audit.Log(bed.IsActive ? "Çarpayı istifadəyə qaytarıldı" : "Çarpayı istifadədən çıxarıldı", AuditEntities.Inpatient, null, bed.Label);
            TempData["Success"] = $"{bed.Label}: {(bed.IsActive ? "istifadədədir" : "istifadədən çıxarıldı")}";
            return RedirectToAction(nameof(Index));
        }

        private static string NextName(Ward w)
        {
            var max = w.Beds.Select(b => int.TryParse(b.Name, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return (max + 1).ToString();
        }

        private IActionResult Back(string error)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }
    }
}
