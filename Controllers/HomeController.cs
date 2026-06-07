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

            // Per-reservation total vs paid (so overpayments don't offset other debts)
            var balances = _context.Reserves
                .Select(r => new
                {
                    Total = r.ReserveExaminationTypes!.Sum(x => (decimal?)x.ExaminationType.Price) ?? 0,
                    Paid = r.Payments!.Sum(p => (decimal?)p.Amount) ?? 0
                })
                .ToList();

            var totalCollected = balances.Sum(b => b.Paid);
            var totalOutstanding = balances.Sum(b => b.Total - b.Paid > 0 ? b.Total - b.Paid : 0);

            var model = new DashboardViewModel
            {
                TotalReservations = _context.Reserves.Count(),
                WaitingReservations = _context.Reserves.Count(r => r.Status != null && r.Status.Name == "Waiting"),
                ConfirmedReservations = _context.Reserves.Count(r => r.Status != null && r.Status.Name == "Confirmed"),
                ExaminationTypeCount = _context.ExaminationTypes.Count(),
                UserCount = _context.AppUsers.Count(),
                TotalIncome = totalCollected,
                TodayIncome = _context.Payments.Where(p => p.PaidAt >= today && p.PaidAt < tomorrow).Sum(p => (decimal?)p.Amount) ?? 0,
                Outstanding = totalOutstanding,
                RecentReservations = _context.Reserves
                    .Include(r => r.Status)
                    .OrderByDescending(r => r.Id)
                    .Take(5)
                    .ToList()
            };
            return View(model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
