using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace ClinicReservation.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;

        public HomeController(ILogger<HomeController> logger, AppDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public IActionResult Index()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var dayAfter = today.AddDays(2);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var myDoctor = userId == null ? null : _context.Doctors.FirstOrDefault(d => d.UserId == userId);

            var active = _context.Reserves.Where(r => r.Status == null
                || (r.Status.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow));
            if (myDoctor != null)
                active = active.Where(r => r.DoctorId == myDoctor.Id);

            var todayQuery = active.Where(r => r.AppointmentAt >= today && r.AppointmentAt < tomorrow);

            var model = new DashboardViewModel
            {
                MyDoctor = myDoctor,
                TodayCount = todayQuery.Count(),
                WaitingRoomCount = todayQuery.Count(r => r.Status!.Name == StatusNames.Arrived),
                InProgressCount = todayQuery.Count(r => r.Status!.Name == StatusNames.InProgress),
                CompletedToday = todayQuery.Count(r => r.Status!.Name == StatusNames.Completed),
                TomorrowCount = active.Count(r => r.AppointmentAt >= tomorrow && r.AppointmentAt < dayAfter),
                WaitingReservations = active.Count(r => r.Status!.Name == StatusNames.Waiting && r.AppointmentAt >= today),
                TodayAppointments = todayQuery
                    .Include(r => r.Status)
                    .Include(r => r.Patient)
                    .Include(r => r.Doctor)
                    .Include(r => r.Room)
                    .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                    .OrderBy(r => r.AppointmentAt)
                    .ToList()
            };

            if (User.Can(Permissions.KassaTotals))
            {
                // Per-reservation due vs paid (so one patient's overpayment doesn't hide another's debt)
                var balances = _context.Reserves
                    .Select(r => new
                    {
                        Due = r.Status != null && (r.Status.Name == StatusNames.Cancelled || r.Status.Name == StatusNames.NoShow)
                            ? 0
                            : r.ReserveExaminationTypes!.Sum(x => (decimal?)x.Price) ?? 0,
                        Paid = r.Payments!.Sum(p => (decimal?)p.Amount) ?? 0
                    })
                    .ToList();
                model.TotalIncome = balances.Sum(b => b.Paid);
                model.Outstanding = balances.Sum(b => b.Due - b.Paid > 0 ? b.Due - b.Paid : 0);
                model.TodayIncome = _context.Payments.Where(p => p.PaidAt >= today && p.PaidAt < tomorrow).Sum(p => (decimal?)p.Amount) ?? 0;
            }

            return View(model);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error(int? code)
        {
            Response.StatusCode = code ?? 500;
            return View(new ErrorViewModel
            {
                StatusCode = code ?? 500,
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
