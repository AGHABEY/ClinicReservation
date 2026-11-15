using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Self-service kiosk: «Gəldim» by FİN / card / QR, and reception or cashier queue tickets. Full screen, no menu.</summary>
    public class KioskController : Controller
    {
        private readonly Kiosk _kiosk;
        private readonly DeskQueue _desk;
        private readonly DeskQueries _deskQueries;

        public KioskController(Kiosk kiosk, DeskQueue desk, DeskQueries deskQueries)
        {
            _kiosk = kiosk;
            _desk = desk;
            _deskQueries = deskQueries;
        }

        [HasPermission(Permissions.QueueKiosk)]
        public IActionResult Index() => View();

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.QueueKiosk)]
        public async Task<IActionResult> Find(string? code, CancellationToken ct)
        {
            var visits = await _kiosk.FindAsync(code, ct);
            ViewBag.Code = code;
            if (visits == null) ViewBag.Error = "Məlumat tapılmadı. FİN kodu yoxlayın və ya registraturaya yaxınlaşın.";
            else if (visits.Count == 0) ViewBag.Error = "Bu gün üçün qəbulunuz yoxdur. Registratura üçün talon götürün.";
            return View("Index", visits);
        }

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.QueueKiosk)]
        public async Task<IActionResult> CheckIn(int id, string? code, CancellationToken ct)
        {
            var (r, res) = await _kiosk.CheckInAsync(id, code, ct);
            if (res != null && res.QueueTicket != null) return RedirectToAction("Ticket", "Queue", new { id = res.Id, kiosk = true });
            ViewBag.Code = code;
            ViewBag.Error = r.ErrorText;
            return View("Index", await _kiosk.FindAsync(code, ct));
        }

        [HttpPost, ValidateAntiForgeryToken, HasPermission(Permissions.QueueKiosk)]
        public async Task<IActionResult> Desk(DeskKind kind, CancellationToken ct)
        {
            var t = await _desk.IssueAsync(kind, QueuePriority.None, Kiosk.Actor, ct);
            return RedirectToAction(nameof(DeskTicket), new { id = t.Id, kiosk = true });
        }

        /// <summary>Printable reception / cashier ticket (kiosk or staff).</summary>
        [HasPermission(Permissions.QueueKiosk, Permissions.DeskServe)]
        public async Task<IActionResult> DeskTicket(int id, bool kiosk, string? back, CancellationToken ct)
        {
            var t = await _deskQueries.TicketAsync(id, ct);
            if (t == null) return NotFound();
            ViewBag.Ahead = await _deskQueries.AheadAsync(t, ct);
            ViewBag.Kiosk = kiosk;
            ViewBag.Back = Url.IsLocalUrl(back) ? back : null;
            return View(t);
        }
    }
}
