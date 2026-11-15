using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Billing;
using ClinicReservation.Web;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.PaymentsTake, Permissions.KassaTotals)]
    public class KassaController : Controller
    {
        private readonly Kassa _kassa;
        public KassaController(Kassa kassa) => _kassa = kassa;

        // filter: unpaid | paid | refund | (empty = all)
        public async Task<IActionResult> Index(string? filter, string? q, CancellationToken ct) =>
            View(await _kassa.ListAsync(filter, q, ct));

        public async Task<IActionResult> Pay(int id, CancellationToken ct) =>
            await _kassa.PaymentPageAsync(id, ct) is { } model ? View(model) : NotFound();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.PaymentsTake)]
        public async Task<IActionResult> Pay(PaymentViewModel input, CancellationToken ct) => Done(await _kassa.PayAsync(input, ct));

        /// <summary>Gives money back when more was paid than is due (e.g. the reservation was cancelled).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.PaymentsRefund)]
        public async Task<IActionResult> Refund(PaymentViewModel input, CancellationToken ct) => Done(await _kassa.RefundAsync(input, ct));

        private IActionResult Done((Result Result, PaymentViewModel? Page) outcome)
        {
            var (r, page) = outcome;
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Pay), new { id = r.Id });
            }
            if (page == null) return NotFound();
            ModelState.AddErrors(r);
            return View(nameof(Pay), page);
        }
    }
}
