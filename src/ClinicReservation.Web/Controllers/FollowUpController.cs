using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Kontrol qəbulları: follow-ups the doctors asked for, as their date approaches.</summary>
    [HasPermission(Permissions.ReservationsView)]
    public class FollowUpController : Controller
    {
        private readonly FollowUpQueries _queries;
        private readonly FollowUps _followUps;

        public FollowUpController(FollowUpQueries queries, FollowUps followUps)
        {
            _queries = queries;
            _followUps = followUps;
        }

        public async Task<IActionResult> Index(FollowUpTab tab, int? doctorId, CancellationToken ct)
        {
            ViewBag.Tab = tab;
            ViewBag.DoctorId = doctorId;
            ViewBag.Doctors = await _queries.DoctorsAsync(ct);
            ViewBag.AheadDays = _queries.AheadDays;
            return View(await _queries.ListAsync(tab, doctorId, ct));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Remind(int? id, CancellationToken ct) => Done(await _followUps.RemindAsync(id, ct));

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Close(int id, string? note, CancellationToken ct) => Done(await _followUps.CloseAsync(id, note, ct));

        private IActionResult Done(Result? r)
        {
            if (r == null) return NotFound();
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                if (r.Warning != null) TempData["Warning"] = r.Warning;
            }
            else TempData["Error"] = r.ErrorText;
            return RedirectToAction(nameof(Index));
        }
    }
}
