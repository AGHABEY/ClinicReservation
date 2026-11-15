using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Həkimin iş masası: today's queue, the current patient and the call button.</summary>
    [HasPermission(Permissions.QueueCall)]
    public class WorkspaceController : Controller
    {
        private readonly QueueDesk _desk;
        private readonly ReservationQueries _reservations;

        public WorkspaceController(QueueDesk desk, ReservationQueries reservations)
        {
            _desk = desk;
            _reservations = reservations;
        }

        public async Task<IActionResult> Index(int? doctorId, CancellationToken ct)
        {
            var mine = await _reservations.DoctorIdForUserAsync(User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
            return await _desk.DeskAsync(doctorId, mine, ct) is { } model ? View(model) : NotFound();
        }

        /// <summary>Calls a patient in: shows the ticket and room on the hall screen.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Call(int id, CancellationToken ct) =>
            await _desk.CallAsync(id, ct) is { } outcome ? Back(outcome) : NotFound();

        /// <summary>Calls the first patient in this doctor's queue who hasn't been called yet.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CallNext(int doctorId, CancellationToken ct) => Back(await _desk.CallNextAsync(doctorId, ct));

        private IActionResult Back((ClinicReservation.Modules.Result Result, int? DoctorId) outcome)
        {
            var (r, doctorId) = outcome;
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Index), new { doctorId });
        }
    }
}
