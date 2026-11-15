using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Controllers
{
    /// <summary>
    /// The link in the reminder SMS: the patient confirms or cancels without
    /// logging in. The random token in the URL is the only key.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Public)]
    [Route("r/{token}")]
    public class PublicController : Controller
    {
        private readonly PatientLinks _links;
        private readonly SmsOptions _sms;

        public PublicController(PatientLinks links, IOptions<SmsOptions> sms)
        {
            _links = links;
            _sms = sms.Value;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(string token, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            ViewBag.Clinic = _sms.ClinicName;
            ViewBag.CanMove = _links.CannotMove(res) == null;
            ViewBag.CanRate = _links.CanRate(res);
            ViewBag.Rating = await _links.RatingAsync(res.Id, ct);
            return View(res);
        }

        [HttpGet("move")]
        public async Task<IActionResult> Move(string token, DateTime? date, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            ViewBag.Why = _links.CannotMove(res);
            var day = (date ?? (res.AppointmentAt.Date > ClinicTime.Today ? res.AppointmentAt.Date : ClinicTime.Today)).Date;
            ViewBag.Date = day;
            ViewBag.Slots = ViewBag.Why == null ? await _links.MoveSlotsAsync(res, day, ct) : new SlotResult();
            return View("Move", res);
        }

        [HttpPost("move")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Move(string token, DateTime? date, string? time, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            var r = await _links.MoveAsync(res, date, time, ct);
            if (r.Ok)
            {
                TempData["PublicDone"] = "moved";
                TempData["PublicMessage"] = r.Message;
                return Redirect($"/r/{token}");
            }
            ViewBag.Why = _links.CannotMove(res);
            ViewBag.Error = r.ErrorText;
            ViewBag.Date = (date ?? ClinicTime.Today).Date;
            ViewBag.Slots = ViewBag.Why == null ? await _links.MoveSlotsAsync(res, (date ?? ClinicTime.Today).Date, ct) : new SlotResult();
            return View("Move", res);
        }

        [HttpPost("rate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rate(string token, int score, string? comment, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            var r = await _links.RateAsync(res, score, comment, ct);
            TempData["PublicDone"] = r.Ok ? "rated" : "rate-error";
            TempData["PublicMessage"] = r.Ok ? r.Message : r.ErrorText;
            return Redirect($"/r/{token}#rate");
        }

        [HttpGet("rate")]
        public IActionResult RateLink(string token) => Redirect($"/r/{token}#rate");

        [HttpPost("confirm")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(string token, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            await _links.ConfirmAsync(res, ct);
            TempData["PublicDone"] = "confirmed";
            return Redirect($"/r/{token}");
        }

        [HttpPost("cancel")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(string token, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            await _links.CancelAsync(res, ct);
            TempData["PublicDone"] = "cancelled";
            return Redirect($"/r/{token}");
        }

        /// <summary>Online lab results: the link from the SMS plus the patient's birth date.</summary>
        [HttpGet("lab")]
        public async Task<IActionResult> Lab(string token, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            ViewBag.Token = token;
            return View("LabLogin", res);
        }

        [HttpPost("lab")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Lab(string token, DateTime? birthday, CancellationToken ct)
        {
            var res = await _links.ByTokenAsync(token, ct);
            if (res == null) return View("NotFound");
            ViewBag.Token = token;
            var (orders, error) = await _links.LabResultsAsync(res, birthday, ct);
            if (error != null)
            {
                ViewBag.Error = error;
                return View("LabLogin", res);
            }
            return View("LabResults", orders);
        }
    }
}
