using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.SmsManage)]
    public class SmsController : Controller
    {
        private readonly SmsDesk _desk;
        public SmsController(SmsDesk desk) => _desk = desk;

        public async Task<IActionResult> Index(SmsStatus? status, CancellationToken ct, int page = 1)
        {
            page = Math.Max(1, page);
            var p = await _desk.PageAsync(status, page, ct);
            ViewBag.Status = status;
            ViewBag.Page = page;
            ViewBag.Total = p.Total;
            ViewBag.PageSize = SmsDesk.PageSize;
            ViewBag.Options = p.Options;
            ViewBag.SentToday = p.SentToday;
            ViewBag.FailedToday = p.FailedToday;
            ViewBag.Pending = p.Pending;
            ViewBag.ConfirmedBySms = p.ConfirmedBySms;
            ViewBag.CancelledBySms = p.CancelledBySms;
            return View(p.Rows);
        }

        /// <summary>Sends (or re-sends) the reminder for one reservation right now.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(int reserveId, CancellationToken ct)
        {
            var r = await _desk.SendReminderAsync(reserveId, ct);
            if (r == null) return NotFound();
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction("Details", "Reservation", new { id = reserveId });
        }

        /// <summary>Runs the reminder job immediately instead of waiting for the next cycle.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunNow(CancellationToken ct)
        {
            TempData["Success"] = (await _desk.RunNowAsync(ct)).Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
