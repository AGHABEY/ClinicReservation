using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Billing;
using ClinicReservation.Modules.Scheduling;
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

        /// <summary>Qəbul hesabatı: load, no-shows, cancellations, lead time, sources, discounts, waiting, ratings.</summary>
        public async Task<IActionResult> Visits(DateTime? from, DateTime? to, int? departmentId, [FromServices] SchedulingReportQueries report, CancellationToken ct) =>
            View(await report.LoadAsync(from, to, departmentId, ct));

        public async Task<IActionResult> VisitsCsv(DateTime? from, DateTime? to, int? departmentId, [FromServices] SchedulingReportQueries report, CancellationToken ct)
        {
            var v = await report.LoadAsync(from, to, departmentId, ct);
            // BOM so Excel reads the Azerbaijani letters.
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(SchedulingReportQueries.Csv(v))).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"qebul-hesabati-{v.From:yyyyMMdd}-{v.To:yyyyMMdd}.csv");
        }
    }
}
