using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.PatientsView)]
    public class PatientController : Controller
    {
        private const int PageSize = 50;
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public PatientController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public IActionResult Index(string? q, int page = 1)
        {
            var query = _context.Patients.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name + " " + p.Surname, term) ||
                    EF.Functions.ILike(p.Surname + " " + p.Name, term) ||
                    EF.Functions.ILike(p.PinCode, term) ||
                    EF.Functions.ILike(p.Phone, term));
            }

            var total = query.Count();
            page = Math.Max(1, page);
            var rows = query
                .OrderBy(p => p.Surname).ThenBy(p => p.Name)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .Select(p => new PatientRow
                {
                    Patient = p,
                    Visits = p.Reserves.Count(r => r.Status!.Name == StatusNames.Completed),
                    LastVisit = p.Reserves.Where(r => r.AppointmentAt < DateTime.Now && r.Status!.Name != StatusNames.Cancelled)
                        .Max(r => (DateTime?)r.AppointmentAt),
                    NextVisit = p.Reserves.Where(r => r.AppointmentAt >= DateTime.Now && ReserveFlow.Active.Contains(r.Status!.Name))
                        .Min(r => (DateTime?)r.AppointmentAt)
                })
                .ToList();

            ViewBag.Query = q;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.PageSize = PageSize;
            return View(rows);
        }

        public IActionResult Details(int id)
        {
            var patient = _context.Patients
                .Include(p => p.Reserves).ThenInclude(r => r.Status)
                .Include(p => p.Reserves).ThenInclude(r => r.Doctor)
                .Include(p => p.Reserves).ThenInclude(r => r.Payments)
                .Include(p => p.Reserves).ThenInclude(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefault(p => p.Id == id);
            if (patient == null)
                return NotFound();

            if (User.Can(Permissions.RecordsView))
            {
                ViewBag.Records = _context.MedicalRecords
                    .Include(m => m.Doctor)
                    .Where(m => m.PatientId == id)
                    .OrderByDescending(m => m.CreatedAt)
                    .ToList();
            }

            if (User.Can(Permissions.LabView))
            {
                ViewBag.LabOrders = _context.LabOrders
                    .Include(o => o.Results)
                    .Where(o => o.PatientId == id && o.Status != LabOrderStatus.Cancelled)
                    .OrderByDescending(o => o.CollectedAt)
                    .Take(20)
                    .ToList();
            }

            _audit.Log("Xəstə kartına baxıldı", AuditEntities.Patient, patient.Id, patient.FullName);
            return View(patient);
        }

        /// <summary>FİN lookup for the reservation form.</summary>
        [HttpGet]
        public IActionResult Lookup(string pin)
        {
            var normalized = Patient.NormalizePin(pin);
            var p = _context.Patients.FirstOrDefault(x => x.PinCode == normalized);
            if (p == null)
                return Json(new { found = false });

            var visits = _context.Reserves.Count(r => r.PatientId == p.Id && r.Status!.Name == StatusNames.Completed);
            return Json(new
            {
                found = true,
                id = p.Id,
                name = p.Name,
                surname = p.Surname,
                phone = p.Phone,
                birthday = p.Birthday.ToString("yyyy-MM-dd"),
                visits,
                url = Url.Action(nameof(Details), new { id = p.Id })
            });
        }

        [HasPermission(Permissions.PatientsEdit)]
        public IActionResult Create() => View("Form", new PatientFormViewModel());

        [HasPermission(Permissions.PatientsEdit)]
        public IActionResult Edit(int id)
        {
            var p = _context.Patients.Find(id);
            if (p == null) return NotFound();
            return View("Form", new PatientFormViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Surname = p.Surname,
                PinCode = p.PinCode,
                Phone = p.Phone,
                Birthday = p.Birthday,
                Notes = p.Notes,
                Gender = p.Gender
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.PatientsEdit)]
        public IActionResult Save(PatientFormViewModel model)
        {
            var patient = model.Id > 0 ? _context.Patients.Find(model.Id) : null;
            if (model.Id > 0 && patient == null) return NotFound();

            var pin = Patient.NormalizePin(model.PinCode);
            if (!string.IsNullOrEmpty(pin) && _context.Patients.Any(p => p.PinCode == pin && p.Id != model.Id))
                ModelState.AddModelError(nameof(model.PinCode), "Bu FİN kodla xəstə artıq mövcuddur");

            if (model.Birthday.HasValue && model.Birthday.Value.Date > DateTime.Today)
                ModelState.AddModelError(nameof(model.Birthday), "Doğum tarixi gələcəkdə ola bilməz");

            if (!ModelState.IsValid)
                return View("Form", model);

            var isNew = patient == null;
            patient ??= new Patient { CreatedAt = DateTime.Now };
            var before = isNew ? null : $"{patient.FullName}, {patient.PinCode}, {patient.Phone}";
            patient.Name = model.Name!.Trim();
            patient.Surname = model.Surname!.Trim();
            patient.PinCode = pin;
            patient.Phone = model.Phone!.Trim();
            patient.Birthday = model.Birthday!.Value.Date;
            patient.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
            patient.Gender = model.Gender;
            if (isNew) _context.Patients.Add(patient);
            _context.SaveChanges();

            _audit.Log(isNew ? "Xəstə kartı yaradıldı" : "Xəstə məlumatları dəyişdirildi", AuditEntities.Patient, patient.Id,
                isNew ? $"{patient.FullName} ({patient.PinCode})" : $"{before} → {patient.FullName}, {patient.PinCode}, {patient.Phone}");

            TempData["Success"] = isNew ? $"{patient.FullName} üçün kart yaradıldı" : "Xəstə məlumatları yeniləndi";
            return RedirectToAction(nameof(Details), new { id = patient.Id });
        }
    }

    public class PatientRow
    {
        public Patient Patient { get; set; } = null!;
        public int Visits { get; set; }
        public DateTime? LastVisit { get; set; }
        public DateTime? NextVisit { get; set; }
    }
}
