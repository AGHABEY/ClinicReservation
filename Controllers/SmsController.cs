using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.SmsManage)]
    public class SmsController : Controller
    {
        private const int PageSize = 50;
        private readonly AppDbContext _context;
        private readonly SmsService _sms;
        private readonly ReminderWorker _worker;
        private readonly AuditService _audit;

        public SmsController(AppDbContext context, SmsService sms, ReminderWorker worker, AuditService audit)
        {
            _context = context;
            _sms = sms;
            _worker = worker;
            _audit = audit;
        }

        public IActionResult Index(SmsStatus? status, int page = 1)
        {
            var query = _context.SmsMessages.Include(m => m.Patient).AsQueryable();
            if (status != null)
                query = query.Where(m => m.Status == status);

            var total = query.Count();
            page = Math.Max(1, page);
            var rows = query.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize).ToList();

            var today = DateTime.Today;
            var now = DateTime.Now;
            var o = _sms.Options;
            ViewBag.Status = status;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.PageSize = PageSize;
            ViewBag.Options = o;
            ViewBag.SentToday = _context.SmsMessages.Count(m => m.CreatedAt >= today && m.Status != SmsStatus.Failed);
            ViewBag.FailedToday = _context.SmsMessages.Count(m => m.CreatedAt >= today && m.Status == SmsStatus.Failed);
            ViewBag.Pending = _context.Reserves.Count(r => r.ReminderSentAt == null && r.AppointmentAt > now
                && r.AppointmentAt <= now.AddHours(o.ReminderHoursBefore)
                && (r.Status!.Name == StatusNames.Waiting || r.Status.Name == StatusNames.Confirmed));
            ViewBag.ConfirmedBySms = _context.AuditLogs.Count(a => a.At >= today.AddDays(-30) && a.Action == "Xəstə SMS linki ilə təsdiq etdi");
            ViewBag.CancelledBySms = _context.AuditLogs.Count(a => a.At >= today.AddDays(-30) && a.Action == "Xəstə SMS linki ilə ləğv etdi");
            return View(rows);
        }

        /// <summary>Sends (or re-sends) the reminder for one reservation right now.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(int reserveId)
        {
            var res = _context.Reserves.Include(r => r.Status).Include(r => r.Patient).Include(r => r.Doctor)
                .FirstOrDefault(r => r.Id == reserveId);
            if (res == null) return NotFound();
            if (res.Status?.Name is not (StatusNames.Waiting or StatusNames.Confirmed) || res.AppointmentAt <= DateTime.Now)
            {
                TempData["Error"] = "Xatırlatma yalnız gələcək, aktiv rezervasiyaya göndərilir";
                return RedirectToAction("Details", "Reservation", new { id = reserveId });
            }

            var msg = await _sms.SendReminderAsync(res, SmsService.KindManual, User.Identity?.Name);
            _audit.Log("SMS göndərildi", AuditEntities.Sms, msg.Id, $"{res.Patient.FullName}, {msg.Phone}: {StatusLabel(msg.Status)}");
            if (msg.Status == SmsStatus.Failed)
                TempData["Error"] = $"SMS göndərilmədi: {msg.Error}";
            else
                TempData["Success"] = msg.Status == SmsStatus.Simulated
                    ? "SMS test rejimində qeydə alındı (həqiqətən göndərilmədi — SMS jurnalına baxın)"
                    : $"SMS {msg.Phone} nömrəsinə göndərildi";
            return RedirectToAction("Details", "Reservation", new { id = reserveId });
        }

        /// <summary>Runs the reminder job immediately instead of waiting for the next cycle.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunNow()
        {
            var count = await _worker.RunOnceAsync(HttpContext.RequestAborted);
            _audit.Log("SMS xatırlatmaları əl ilə işə salındı", AuditEntities.Sms, null, $"{count} SMS");
            TempData["Success"] = count == 0
                ? "Göndəriləcək xatırlatma yoxdur (və ya indi sakit saatlardır)"
                : $"{count} xatırlatma göndərildi";
            return RedirectToAction(nameof(Index));
        }

        public static string StatusLabel(SmsStatus s) => s switch
        {
            SmsStatus.Sent => "Göndərildi",
            SmsStatus.Simulated => "Test rejimi",
            _ => "Xəta"
        };
    }
}
