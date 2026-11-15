using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace ClinicReservation.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {

        public async Task<IActionResult> Index([FromServices] DashboardQueries dashboard, CancellationToken ct) =>
            View(await dashboard.TodayAsync(User.FindFirstValue(ClaimTypes.NameIdentifier), User.Can(Permissions.KassaTotals), ct));

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
