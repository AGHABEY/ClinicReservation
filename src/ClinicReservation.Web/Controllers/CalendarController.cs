using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Təqvim: doctors side by side for a day, or one doctor's week; drag a visit to move it.</summary>
    [HasPermission(Permissions.ReservationsView)]
    public class CalendarController : Controller
    {
        private readonly CalendarQueries _queries;
        private readonly Reservations _reservations;

        public CalendarController(CalendarQueries queries, Reservations reservations)
        {
            _queries = queries;
            _reservations = reservations;
        }

        public async Task<IActionResult> Index(string? mode, DateTime? date, int? departmentId, int? doctorId, CancellationToken ct) =>
            View(await _queries.LoadAsync(mode, date, departmentId, doctorId, ct));

        /// <summary>JSON for the drag-and-drop: { ok, message }.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Move(int id, int doctorId, DateTime date, string time, CancellationToken ct)
        {
            var r = await _reservations.MoveAsync(id, doctorId, date, time, ct);
            if (r == null) return NotFound();
            return Json(new { ok = r.Ok, message = r.Ok ? r.Message : r.ErrorText });
        }
    }
}
