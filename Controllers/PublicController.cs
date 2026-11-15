using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private readonly AppDbContext _context;
        private readonly AuditService _audit;
        private readonly SmsOptions _sms;

        public PublicController(AppDbContext context, AuditService audit, IOptions<SmsOptions> sms)
        {
            _context = context;
            _audit = audit;
            _sms = sms.Value;
        }

        [HttpGet("")]
        public IActionResult Index(string token)
        {
            var res = Load(token);
            if (res == null) return View("NotFound");
            ViewBag.Clinic = _sms.ClinicName;
            return View(res);
        }

        [HttpPost("confirm")]
        [ValidateAntiForgeryToken]
        public IActionResult Confirm(string token)
        {
            var res = Load(token);
            if (res == null) return View("NotFound");
            if (CanAnswer(res) && res.Status?.Name == StatusNames.Waiting)
            {
                res.Status = _context.ReserveStatuses.First(s => s.Name == StatusNames.Confirmed);
                res.ConfirmDate = DateTime.Now;
                _context.SaveChanges();
                _audit.Log("Xəstə SMS linki ilə təsdiq etdi", AuditEntities.Reservation, res.Id, res.Patient.FullName, userName: "Xəstə (SMS linki)");
            }
            TempData["PublicDone"] = "confirmed";
            return Redirect($"/r/{token}");
        }

        [HttpPost("cancel")]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(string token)
        {
            var res = Load(token);
            if (res == null) return View("NotFound");
            if (CanAnswer(res))
            {
                res.Status = _context.ReserveStatuses.First(s => s.Name == StatusNames.Cancelled);
                res.CancelDate = DateTime.Now;
                res.CancelReason = "Xəstə SMS linki ilə ləğv etdi";
                _context.SaveChanges();
                _audit.Log("Xəstə SMS linki ilə ləğv etdi", AuditEntities.Reservation, res.Id, res.Patient.FullName, userName: "Xəstə (SMS linki)");
            }
            TempData["PublicDone"] = "cancelled";
            return Redirect($"/r/{token}");
        }

        /// <summary>Only before the visit and while the booking is still open.</summary>
        public static bool CanAnswer(Reserve r) =>
            r.AppointmentAt > DateTime.Now && r.Status?.Name is StatusNames.Waiting or StatusNames.Confirmed;

        private Reserve? Load(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;
            return _context.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.Room)
                .FirstOrDefault(r => r.PatientToken == token);
        }
    }
}
