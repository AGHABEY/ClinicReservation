using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicReservation.Controllers
{
    /// <summary>Onlayn yazılış portalı: doctor → services → free time → details → SMS code.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Public)]
    [Route("online")]
    public class OnlineController : Controller
    {
        private readonly OnlineBooking _online;
        public OnlineController(OnlineBooking online) => _online = online;

        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            if (!_online.Options.OnlineEnabled) return View("Closed");
            return View(await _online.DoctorsAsync(ct));
        }

        /// <summary>Step 2: services, day and time, then the patient's details (one page).</summary>
        [HttpGet("doctor/{id:int}")]
        public async Task<IActionResult> Doctor(int id, DateTime? date, [FromQuery] List<int>? examIds, CancellationToken ct)
        {
            var doctor = (await _online.DoctorsAsync(ct)).FirstOrDefault(d => d.Doctor.Id == id);
            if (doctor == null) return RedirectToAction(nameof(Index));
            var form = new OnlineBookingForm { DoctorId = id, Date = (date ?? ClinicTime.Today).Date, ExamIds = examIds is { Count: > 0 } ? examIds : new() { doctor.Exams[0].Id } };
            return Step(doctor, form);
        }

        [HttpPost("doctor/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Doctor(int id, OnlineBookingForm form, CancellationToken ct)
        {
            var doctor = (await _online.DoctorsAsync(ct)).FirstOrDefault(d => d.Doctor.Id == id);
            if (doctor == null) return RedirectToAction(nameof(Index));
            form.DoctorId = id;
            var errors = ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList();
            var (r, key) = await _online.StartAsync(form, errors, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            if (r.Ok) return RedirectToAction(nameof(Verify), new { key });
            foreach (var (field, message) in r.Errors)
                if (!(ModelState.TryGetValue(field ?? "", out var e) && e.Errors.Any(x => x.ErrorMessage == message)))
                    ModelState.AddModelError(field ?? "", message);
            return Step(doctor, form);
        }

        private IActionResult Step(OnlineDoctor doctor, OnlineBookingForm form)
        {
            ViewBag.Doctor = doctor;
            ViewBag.Slots = form.Date == null ? new SlotResult() : _online.Slots(doctor.Doctor.Id, form.Date.Value, form.ExamIds ?? new());
            ViewBag.MaxDate = ClinicTime.Today.AddDays(_online.Options.OnlineMaxDaysAhead);
            return View("Doctor", form);
        }

        [HttpGet("verify/{key:guid}")]
        public async Task<IActionResult> Verify(Guid key, CancellationToken ct)
        {
            var pending = await _online.PendingAsync(key, ct);
            if (pending == null) return View("Expired");
            ViewBag.Phone = OnlineBooking.Mask(pending.Value.Otp.Phone);
            ViewBag.Key = key;
            return View();
        }

        [HttpPost("verify/{key:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(Guid key, string? code, CancellationToken ct)
        {
            var (r, token) = await _online.ConfirmAsync(key, code, ct);
            if (r.Ok) return Redirect($"/r/{token}?new=1");
            var pending = await _online.PendingAsync(key, ct);
            if (pending == null) { ViewBag.Error = r.ErrorText; return View("Expired"); }
            ViewBag.Phone = OnlineBooking.Mask(pending.Value.Otp.Phone);
            ViewBag.Key = key;
            ViewBag.Error = r.ErrorText;
            return View();
        }
    }

    public static class RateLimits
    {
        /// <summary>Per-address limit for the anonymous pages (online booking, the SMS link).</summary>
        public const string Public = "public";
    }
}
