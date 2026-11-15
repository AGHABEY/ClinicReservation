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

        private const string LabFailAction = "Onlayn nəticə: səhv doğum tarixi";

        /// <summary>Online lab results: the link from the SMS plus the patient's birth date.</summary>
        [HttpGet("lab")]
        public IActionResult Lab(string token)
        {
            var res = Load(token);
            if (res == null) return View("NotFound");
            ViewBag.Token = token;
            return View("LabLogin", res);
        }

        [HttpPost("lab")]
        [ValidateAntiForgeryToken]
        public IActionResult Lab(string token, DateTime? birthday)
        {
            var res = Load(token);
            if (res == null) return View("NotFound");
            ViewBag.Token = token;

            var since = DateTime.Now.AddHours(-1);
            var failures = _context.AuditLogs.Count(a => a.Action == LabFailAction && a.EntityId == res.Id.ToString() && a.At >= since);
            if (failures >= 5)
            {
                ViewBag.Error = "Çox sayda səhv cəhd. Bir saat sonra yenidən yoxlayın və ya klinikaya zəng edin.";
                return View("LabLogin", res);
            }
            if (birthday?.Date != res.Patient.Birthday.Date)
            {
                _audit.Log(LabFailAction, AuditEntities.Lab, res.Id, res.Patient.FullName, userName: "Xəstə (SMS linki)");
                ViewBag.Error = "Doğum tarixi uyğun gəlmir";
                return View("LabLogin", res);
            }

            var orders = _context.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Reserve).ThenInclude(r => r.Doctor)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .Where(o => o.ReserveId == res.Id && o.Status == LabOrderStatus.Validated)
                .OrderBy(o => o.CollectedAt)
                .AsSplitQuery()
                .ToList();
            _audit.Log("Xəstə nəticəyə onlayn baxdı", AuditEntities.Lab, res.Id, res.Patient.FullName, userName: "Xəstə (SMS linki)");
            return View("LabResults", orders);
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
