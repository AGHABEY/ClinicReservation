using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Patients' scores of their visits: averages per doctor and the comments (low scores first to act on).</summary>
    [HasPermission(Permissions.ReportsView)]
    public class RatingController : Controller
    {
        private readonly RatingQueries _ratings;
        public RatingController(RatingQueries ratings) => _ratings = ratings;

        public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? doctorId, bool low, CancellationToken ct)
        {
            var t = (to ?? ClinicTime.Today).Date;
            var f = (from ?? t.AddDays(-30)).Date;
            ViewBag.From = f;
            ViewBag.To = t;
            ViewBag.DoctorId = doctorId;
            ViewBag.Low = low;
            ViewBag.ByDoctor = await _ratings.ByDoctorAsync(f, t, ct);
            return View(await _ratings.ListAsync(doctorId, low ? 2 : null, f, t, ct));
        }
    }
}
