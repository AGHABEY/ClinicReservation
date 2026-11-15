using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Elektron növbə: the waiting-hall screen and printed tickets.</summary>
    public class QueueController : Controller
    {
        /// <summary>How long a call stays in the "called" column of the screen.</summary>
        private static readonly TimeSpan CallVisible = TimeSpan.FromMinutes(10);

        private readonly AppDbContext _context;

        public QueueController(AppDbContext context) => _context = context;

        /// <summary>Full-screen page for the TV in the waiting hall (?departmentId= for one floor/department).</summary>
        [HasPermission(Permissions.QueueView)]
        public IActionResult Screen(int? departmentId)
        {
            ViewBag.DepartmentId = departmentId;
            ViewBag.Department = departmentId == null ? null : _context.Departments.Find(departmentId)?.Name;
            ViewBag.Departments = _context.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
            return View();
        }

        /// <summary>Polled by the screen every few seconds. Shows tickets only — no patient names.</summary>
        [HasPermission(Permissions.QueueView)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Data(int? departmentId)
        {
            var now = ClinicTime.Now;
            var today = now.Date;
            var query = _context.Reserves
                .Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1) && r.QueueTicket != null);
            if (departmentId != null)
                query = query.Where(r => r.Doctor != null && r.Doctor.DepartmentId == departmentId);

            var since = now - CallVisible;
            var called = query
                .Where(r => r.CalledAt != null && r.CalledAt >= since
                            && (r.Status!.Name == StatusNames.Arrived || r.Status.Name == StatusNames.InProgress))
                .OrderByDescending(r => r.CalledAt)
                .Take(6)
                .Select(r => new
                {
                    id = r.Id,
                    ticket = r.QueueTicket,
                    room = r.Room != null ? r.Room.Name : (r.Doctor != null && r.Doctor.Room != null ? r.Doctor.Room.Name : null),
                    doctor = r.Doctor != null ? r.Doctor.FullName : null,
                    calledAt = r.CalledAt
                })
                .ToList();

            var waiting = query
                .Where(r => r.Status!.Name == StatusNames.Arrived && r.CalledAt == null)
                .OrderBy(r => r.ArrivedAt)
                .Take(30)
                .Select(r => new
                {
                    ticket = r.QueueTicket,
                    doctor = r.Doctor != null ? r.Doctor.FullName : null,
                    since = r.ArrivedAt
                })
                .ToList();

            return Json(new
            {
                now = now.ToString("HH:mm"),
                called = called.Select(c => new { c.id, c.ticket, c.room, doctor = c.doctor == null ? null : Helpers.Fmt.Doctor(c.doctor), key = $"{c.id}:{c.calledAt:HHmmss}" }),
                waiting = waiting.Select(w => new { w.ticket, w.doctor, minutes = w.since == null ? 0 : (int)(now - w.since.Value).TotalMinutes })
            });
        }

        /// <summary>Small printable ticket handed to the patient at check-in.</summary>
        [HasPermission(Permissions.ReservationsView)]
        public IActionResult Ticket(int id)
        {
            var res = _context.Reserves
                .Include(r => r.Patient)
                .Include(r => r.Doctor).ThenInclude(d => d!.Room)
                .Include(r => r.Room)
                .FirstOrDefault(r => r.Id == id);
            if (res == null) return NotFound();
            if (res.QueueTicket == null)
            {
                TempData["Error"] = "Bu rezervasiyaya hələ növbə talonu verilməyib (xəstə «Gəldi» kimi qeyd edilməyib)";
                return RedirectToAction("Details", "Reservation", new { id });
            }
            var waitingAhead = _context.Reserves.Count(r => r.DoctorId == res.DoctorId && r.Id != res.Id
                && r.Status!.Name == StatusNames.Arrived && r.ArrivedAt < res.ArrivedAt
                && r.AppointmentAt >= ClinicTime.Today && r.AppointmentAt < ClinicTime.Today.AddDays(1));
            ViewBag.Ahead = waitingAhead;
            return View(res);
        }
    }
}
