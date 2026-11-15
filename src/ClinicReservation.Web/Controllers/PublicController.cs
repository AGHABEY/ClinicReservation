using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Controllers
{
    /// <summary>
    /// The link in the reminder SMS: the patient confirms or cancels without
    /// logging in. The random token in the URL is the only key.
    /// </summary>
    [AllowAnonymous]
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
            return View(res);
        }

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
