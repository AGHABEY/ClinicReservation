using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Elektron növbə: the waiting-hall screen and printed tickets.</summary>
    public class QueueController : Controller
    {
        private readonly QueueDesk _desk;
        public QueueController(QueueDesk desk) => _desk = desk;

        /// <summary>Full-screen page for the TV in the waiting hall (?departmentId= for one floor/department).</summary>
        [HasPermission(Permissions.QueueView)]
        public async Task<IActionResult> Screen(int? departmentId, CancellationToken ct)
        {
            ViewBag.DepartmentId = departmentId;
            ViewBag.Department = departmentId == null ? null : await _desk.DepartmentNameAsync(departmentId.Value, ct);
            ViewBag.Departments = await _desk.ActiveDepartmentsAsync(ct);
            return View();
        }

        /// <summary>Polled by the screen every few seconds. Shows tickets only — no patient names.</summary>
        [HasPermission(Permissions.QueueView)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Data(int? departmentId, CancellationToken ct) => Json(await _desk.ScreenDataAsync(departmentId, ct));

        /// <summary>Small printable ticket handed to the patient at check-in.</summary>
        [HasPermission(Permissions.ReservationsView, Permissions.QueueKiosk)]
        public async Task<IActionResult> Ticket(int id, bool kiosk, CancellationToken ct)
        {
            var found = await _desk.TicketAsync(id, ct);
            if (found == null) return NotFound();
            var (res, ahead) = found.Value;
            // The kiosk prints today's tickets only and never links into the reservation.
            kiosk |= !User.Can(Permissions.ReservationsView);
            if (kiosk && res.ArrivedAt?.Date != ClinicTime.Today) return NotFound();
            ViewBag.Kiosk = kiosk;
            if (res.QueueTicket == null)
            {
                TempData["Error"] = "Bu rezervasiyaya hələ növbə talonu verilməyib (xəstə «Gəldi» kimi qeyd edilməyib)";
                return RedirectToAction("Details", "Reservation", new { id });
            }
            ViewBag.Ahead = ahead;
            return View(res);
        }
    }
}
