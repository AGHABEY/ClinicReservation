using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>A reception / cashier window: call the next ticket, call again, served, missed; print a ticket at the door.</summary>
    [HasPermission(Permissions.DeskServe)]
    public class DeskController : Controller
    {
        private readonly DeskQueue _queue;
        private readonly DeskQueries _queries;

        public DeskController(DeskQueue queue, DeskQueries queries)
        {
            _queue = queue;
            _queries = queries;
        }

        public async Task<IActionResult> Index(int? windowId, CancellationToken ct) => View(await _queries.WindowAsync(windowId, ct));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Next(int windowId, CancellationToken ct)
        {
            var r = await _queue.CallNextAsync(windowId, ct);
            if (r == null) return NotFound();
            TempData[r.Value.Result.Ok ? "Success" : "Error"] = r.Value.Result.Ok ? r.Value.Result.Message : r.Value.Result.ErrorText;
            return RedirectToAction(nameof(Index), new { windowId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Recall(int id, int windowId, CancellationToken ct) => Back(await _queue.RecallAsync(id, ct), windowId);

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Finish(int id, int windowId, bool missed, CancellationToken ct) => Back(await _queue.FinishAsync(id, missed, ct), windowId);

        /// <summary>Staff prints a ticket for someone at the door (e.g. with priority).</summary>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Issue(DeskKind kind, QueuePriority priority, int? windowId, CancellationToken ct)
        {
            var t = await _queue.IssueAsync(kind, priority, User.Identity?.Name, ct);
            return RedirectToAction("DeskTicket", "Kiosk", new { id = t.Id, back = Url.Action(nameof(Index), new { windowId }) });
        }

        private IActionResult Back(Result? r, int windowId)
        {
            if (r == null) return NotFound();
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Index), new { windowId });
        }
    }
}
