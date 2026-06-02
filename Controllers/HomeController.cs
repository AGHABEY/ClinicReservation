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
            var model = new DashboardViewModel
            {
                TotalReservations = _context.Reserves.Count(),
                WaitingReservations = _context.Reserves.Count(r => r.Status != null && r.Status.Name == "Waiting"),
                ConfirmedReservations = _context.Reserves.Count(r => r.Status != null && r.Status.Name == "Confirmed"),
                ExaminationTypeCount = _context.ExaminationTypes.Count(),
                UserCount = _context.AppUsers.Count(),
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
