using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Billing;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.ReportsView)]
    public class ReportController : Controller
    {
        private readonly ReportQueries _reports;
        public ReportController(ReportQueries reports) => _reports = reports;

        public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken ct) =>
            View(await _reports.PeriodAsync(from, to, ct));
    }
}
