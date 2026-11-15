using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Həkimin iş masası: today's queue, the current patient and the call button.</summary>
    [HasPermission(Permissions.QueueCall)]
    public class WorkspaceController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public WorkspaceController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public IActionResult Index(int? doctorId)
        {
            var myDoctor = MyDoctorId();
            doctorId ??= myDoctor;

            var doctors = _context.Doctors.Where(d => d.IsActive || d.Id == doctorId).OrderBy(d => d.FullName).ToList();
            if (doctorId == null && doctors.Count == 1)
                doctorId = doctors[0].Id;

            var model = new WorkspaceViewModel { Doctors = doctors, IsOwnDesk = doctorId != null && doctorId == myDoctor };
            if (doctorId != null)
            {
                model.Doctor = _context.Doctors.Include(d => d.Room).Include(d => d.Department).FirstOrDefault(d => d.Id == doctorId);
                if (model.Doctor == null) return NotFound();

                var today = ClinicTime.Today;
                model.Today = _context.Reserves
                    .Include(r => r.Status)
                    .Include(r => r.Patient)
                    .Include(r => r.Room)
                    .Include(r => r.MedicalRecord)
                    .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                    .Where(r => r.DoctorId == doctorId && r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1))
                    .Where(r => r.Status!.Name != StatusNames.Cancelled)
                    .OrderBy(r => r.AppointmentAt)
                    .AsSplitQuery()
                    .ToList();
            }
            return View(model);
        }

        /// <summary>Calls a patient in: shows the ticket and room on the hall screen.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Call(int id)
        {
            var res = _context.Reserves.Include(r => r.Status).Include(r => r.Patient).FirstOrDefault(r => r.Id == id);
            if (res == null) return NotFound();
            if (res.Status?.Name is not (StatusNames.Arrived or StatusNames.InProgress))
            {
                TempData["Error"] = "Yalnız gəlmiş xəstəni çağırmaq olar";
                return RedirectToAction(nameof(Index), new { doctorId = res.DoctorId });
            }

            var again = res.CalledAt != null;
            res.CalledAt = ClinicTime.Now;
            _context.SaveChanges();
            _audit.Log(again ? "Xəstə təkrar çağırıldı" : "Xəstə çağırıldı", AuditEntities.Reservation, res.Id,
                $"{res.Patient.FullName}, talon {res.QueueTicket ?? "—"}");
            TempData["Success"] = $"{res.QueueTicket ?? res.Patient.FullName} çağırıldı — ekranda göstərilir";
            return RedirectToAction(nameof(Index), new { doctorId = res.DoctorId });
        }

        /// <summary>Calls the first patient in this doctor's queue who hasn't been called yet.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CallNext(int doctorId)
        {
            var next = QueueService.WaitingFor(_context.Reserves.Where(r => r.DoctorId == doctorId), ClinicTime.Today)
                .Select(r => (int?)r.Id).FirstOrDefault();
            if (next == null)
            {
                TempData["Error"] = "Növbədə gözləyən xəstə yoxdur";
                return RedirectToAction(nameof(Index), new { doctorId });
            }
            return Call(next.Value);
        }

        private int? MyDoctorId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return userId == null ? null : _context.Doctors.Where(d => d.UserId == userId).Select(d => (int?)d.Id).FirstOrDefault();
        }
    }

    public class WorkspaceViewModel
    {
        public List<Doctor> Doctors { get; set; } = new();
        public Doctor? Doctor { get; set; }
        public bool IsOwnDesk { get; set; }
        public List<Reserve> Today { get; set; } = new();

        public Reserve? Current => Today.Where(r => r.Status?.Name == StatusNames.InProgress).OrderBy(r => r.StartedAt).FirstOrDefault();

        public List<Reserve> Queue => Today.Where(r => r.Status?.Name == StatusNames.Arrived)
            .OrderBy(r => r.CalledAt != null).ThenBy(r => r.QueueNumber ?? int.MaxValue).ThenBy(r => r.ArrivedAt).ToList();

        public List<Reserve> Upcoming => Today.Where(r => r.Status?.Name is StatusNames.Waiting or StatusNames.Confirmed).ToList();

        public List<Reserve> Done => Today.Where(r => r.Status?.Name is StatusNames.Completed or StatusNames.NoShow)
            .OrderByDescending(r => r.CompletedAt ?? r.AppointmentAt).ToList();

        /// <summary>Average minutes between check-in and the start of the visit, today.</summary>
        public int? AverageWaitMinutes
        {
            get
            {
                var waits = Today.Where(r => r.ArrivedAt != null && r.StartedAt != null)
                    .Select(r => (r.StartedAt!.Value - r.ArrivedAt!.Value).TotalMinutes).ToList();
                return waits.Count == 0 ? null : (int)Math.Round(waits.Average());
            }
        }
    }
}
