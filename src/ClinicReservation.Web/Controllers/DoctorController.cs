using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.CatalogManage, Permissions.ReservationsView)]
    public class DoctorController : Controller
    {
        public static readonly DayOfWeek[] Week =
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        private readonly AppDbContext _context;
        private readonly AuditService _audit;
        private readonly ClinicOptions _clinic;

        public DoctorController(AppDbContext context, AuditService audit, IOptions<ClinicOptions> clinic)
        {
            _context = context;
            _audit = audit;
            _clinic = clinic.Value;
        }

        public IActionResult Index()
        {
            var today = ClinicTime.Today;
            var doctors = _context.Doctors
                .Include(d => d.Department)
                .Include(d => d.Room)
                .Include(d => d.WorkHours)
                .Include(d => d.Leaves)
                .OrderByDescending(d => d.IsActive).ThenBy(d => d.FullName)
                .ToList();

            ViewBag.TodayCounts = _context.Reserves
                .Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1) && r.DoctorId != null)
                .Where(r => r.Status!.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow)
                .GroupBy(r => r.DoctorId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionary(x => x.Key, x => x.Count);
            return View(doctors);
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Create()
        {
            var model = new DoctorFormViewModel
            {
                SlotMinutes = _clinic.SlotMinutes,
                Days = Week.Select(d => new WorkDayInput
                {
                    Day = d,
                    Works = d is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                    Start = _clinic.WorkStart.ToString(@"hh\:mm"),
                    End = _clinic.WorkEnd.ToString(@"hh\:mm"),
                    BreakStart = "13:00",
                    BreakEnd = "14:00"
                }).ToList()
            };
            PrepareForm();
            return View("Form", model);
        }

        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Edit(int id)
        {
            var d = _context.Doctors.Include(x => x.WorkHours).Include(x => x.Leaves).FirstOrDefault(x => x.Id == id);
            if (d == null) return NotFound();

            var model = new DoctorFormViewModel
            {
                Id = d.Id,
                FullName = d.FullName,
                Specialty = d.Specialty,
                Phone = d.Phone,
                DepartmentId = d.DepartmentId,
                RoomId = d.RoomId,
                SlotMinutes = d.SlotMinutes,
                IsActive = d.IsActive,
                UserId = d.UserId,
                Days = Week.Select(day =>
                {
                    var w = d.WorkHours.FirstOrDefault(x => x.Day == day);
                    return new WorkDayInput
                    {
                        Day = day,
                        Works = w != null,
                        Start = (w?.Start ?? _clinic.WorkStart).ToString(@"hh\:mm"),
                        End = (w?.End ?? _clinic.WorkEnd).ToString(@"hh\:mm"),
                        BreakStart = w?.BreakStart?.ToString(@"hh\:mm"),
                        BreakEnd = w?.BreakEnd?.ToString(@"hh\:mm")
                    };
                }).ToList()
            };
            PrepareForm(d);
            return View("Form", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult Save(DoctorFormViewModel model)
        {
            var doctor = model.Id > 0
                ? _context.Doctors.Include(x => x.WorkHours).Include(x => x.Leaves).FirstOrDefault(x => x.Id == model.Id)
                : null;
            if (model.Id > 0 && doctor == null) return NotFound();

            // Parse and validate the weekly schedule.
            var hours = new List<DoctorWorkHours>();
            for (var i = 0; i < model.Days.Count; i++)
            {
                var d = model.Days[i];
                if (!d.Works) continue;
                var label = Fmt.DayName(d.Day);
                if (!TryTime(d.Start, out var start) || !TryTime(d.End, out var end) || end <= start)
                {
                    ModelState.AddModelError($"Days[{i}].Start", $"{label}: iş saatı düzgün deyil");
                    continue;
                }
                TimeSpan? bs = null, be = null;
                var hasBreak = !string.IsNullOrWhiteSpace(d.BreakStart) || !string.IsNullOrWhiteSpace(d.BreakEnd);
                if (hasBreak)
                {
                    if (!TryTime(d.BreakStart, out var b1) || !TryTime(d.BreakEnd, out var b2) || b2 <= b1 || b1 < start || b2 > end)
                    {
                        ModelState.AddModelError($"Days[{i}].BreakStart", $"{label}: fasilə iş saatının içində olmalıdır");
                        continue;
                    }
                    bs = b1; be = b2;
                }
                hours.Add(new DoctorWorkHours { Day = d.Day, Start = start, End = end, BreakStart = bs, BreakEnd = be });
            }

            if (model.IsActive && !model.Days.Any(d => d.Works))
                ModelState.AddModelError("", "Ən azı bir iş günü seçin");

            if (!string.IsNullOrEmpty(model.UserId)
                && _context.Doctors.Any(x => x.UserId == model.UserId && x.Id != model.Id))
                ModelState.AddModelError(nameof(model.UserId), "Bu istifadəçi artıq başqa həkimə bağlıdır");

            if (!ModelState.IsValid)
            {
                PrepareForm(doctor);
                return View("Form", model);
            }

            var isNew = doctor == null;
            doctor ??= new Doctor();
            doctor.FullName = model.FullName!.Trim();
            doctor.Specialty = string.IsNullOrWhiteSpace(model.Specialty) ? null : model.Specialty.Trim();
            doctor.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
            doctor.DepartmentId = model.DepartmentId;
            doctor.RoomId = model.RoomId;
            doctor.SlotMinutes = model.SlotMinutes;
            doctor.IsActive = model.IsActive;
            doctor.UserId = string.IsNullOrEmpty(model.UserId) ? null : model.UserId;

            _context.DoctorWorkHours.RemoveRange(doctor.WorkHours);
            doctor.WorkHours = hours;
            if (isNew) _context.Doctors.Add(doctor);
            _context.SaveChanges();

            _audit.Log(isNew ? "Həkim əlavə edildi" : "Həkim məlumatları dəyişdirildi", AuditEntities.Doctor, doctor.Id,
                $"{doctor.FullName}; iş günləri: {string.Join(", ", hours.Select(h => $"{Fmt.DayShortName(h.Day)} {h.Start:hh\\:mm}-{h.End:hh\\:mm}"))}");

            TempData["Success"] = isNew ? $"{doctor.FullName} əlavə edildi" : $"{doctor.FullName} yeniləndi";
            return RedirectToAction(nameof(Edit), new { id = doctor.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult AddLeave(LeaveInput input)
        {
            var doctor = _context.Doctors.Find(input.DoctorId);
            if (doctor == null) return NotFound();

            if (!ModelState.IsValid || input.From > input.To)
            {
                TempData["Error"] = "Məzuniyyət tarixləri düzgün deyil";
                return RedirectToAction(nameof(Edit), new { id = input.DoctorId });
            }

            var from = input.From!.Value.Date;
            var to = input.To!.Value.Date;
            _context.DoctorLeaves.Add(new DoctorLeave
            {
                DoctorId = doctor.Id,
                From = from,
                To = to,
                Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim()
            });
            _context.SaveChanges();
            _audit.Log("Məzuniyyət əlavə edildi", AuditEntities.Doctor, doctor.Id, $"{doctor.FullName}: {Fmt.Date(from)} – {Fmt.Date(to)}");

            // Warn about bookings that now fall on the leave.
            var clashes = _context.Reserves.Count(r => r.DoctorId == doctor.Id
                && r.AppointmentAt >= from && r.AppointmentAt < to.AddDays(1)
                && ReserveFlow.Active.Contains(r.Status!.Name));
            if (clashes > 0)
                TempData["Error"] = $"Diqqət: bu tarixlərdə həkimin {clashes} aktiv rezervasiyası var — onları başqa vaxta keçirin.";
            else
                TempData["Success"] = "Məzuniyyət əlavə edildi";

            return RedirectToAction(nameof(Edit), new { id = doctor.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public IActionResult DeleteLeave(int id)
        {
            var leave = _context.DoctorLeaves.Include(l => l.Doctor).FirstOrDefault(l => l.Id == id);
            if (leave == null) return NotFound();
            _context.DoctorLeaves.Remove(leave);
            _context.SaveChanges();
            _audit.Log("Məzuniyyət silindi", AuditEntities.Doctor, leave.DoctorId, $"{leave.Doctor.FullName}: {Fmt.Date(leave.From)} – {Fmt.Date(leave.To)}");
            TempData["Success"] = "Məzuniyyət silindi";
            return RedirectToAction(nameof(Edit), new { id = leave.DoctorId });
        }

        private void PrepareForm(Doctor? doctor = null)
        {
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive || (doctor != null && d.Id == doctor.DepartmentId)).OrderBy(d => d.Name).ToList();
            ViewBag.Rooms = _context.Rooms.Where(r => r.IsActive || (doctor != null && r.Id == doctor.RoomId)).OrderBy(r => r.Name).ToList();
            ViewBag.Users = _context.AppUsers.OrderBy(u => u.UserName).ToList();
            ViewBag.Leaves = doctor?.Leaves.Where(l => l.To >= ClinicTime.Today.AddDays(-30)).OrderBy(l => l.From).ToList() ?? new List<DoctorLeave>();
        }

        private static bool TryTime(string? s, out TimeSpan t) =>
            TimeSpan.TryParseExact(s ?? "", @"hh\:mm", null, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);
    }
}
