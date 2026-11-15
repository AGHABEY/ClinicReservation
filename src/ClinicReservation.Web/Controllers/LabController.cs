using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Lab;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Laboratoriya: sample collection, result entry, validation and reports.</summary>
    [HasPermission(Permissions.LabView, Permissions.LabWork, Permissions.LabValidate)]
    public class LabController : Controller
    {
        private readonly LabQueries _queries;
        private readonly LabWork _work;

        public LabController(LabQueries queries, LabWork work)
        {
            _queries = queries;
            _work = work;
        }

        private bool CanEdit => User.CanAny(Permissions.LabWork, Permissions.LabValidate);

        // tab: pending (sample not taken yet) | work (results being entered) | done (validated)
        public async Task<IActionResult> Index(string? tab, DateTime? date, string? q, CancellationToken ct)
        {
            q = q?.Trim();
            // A barcode scanner types the 10 digits + Enter into the search box.
            if (!string.IsNullOrEmpty(q) && q.Length == 10 && q.All(char.IsDigit))
            {
                if (await _queries.OrderIdByBarcodeAsync(q, ct) is { } hit) return RedirectToAction(nameof(Order), new { id = hit });
                TempData["Error"] = $"{q} barkodlu nümunə tapılmadı";
            }
            return View(await _queries.WorklistAsync(tab, (date ?? ClinicTime.Today).Date, q, ct));
        }

        /// <summary>Takes the sample: creates the order with a barcode and opens the label.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork)]
        public async Task<IActionResult> Collect(int reserveId, string? returnTo, CancellationToken ct)
        {
            var outcome = await _work.CollectAsync(reserveId, ct);
            if (outcome == null) return NotFound();
            var r = outcome.Result;
            if (!r.Ok)
            {
                TempData["Error"] = r.ErrorText;
                return RedirectToAction(nameof(Index));
            }
            TempData["Success"] = r.Message;
            if (r.Warning != null) TempData["Error"] = r.Warning;
            TempData["PrintLabels"] = outcome.LabelIds;
            return returnTo == "reservation"
                ? RedirectToAction("Details", "Reservation", new { id = reserveId })
                : RedirectToAction(nameof(Index), new { tab = "work" });
        }

        public async Task<IActionResult> Order(int id, CancellationToken ct)
        {
            var order = await _queries.OrderAsync(id, ct);
            if (order == null) return NotFound();
            _work.OrderViewed(order);
            return View(_queries.OrderPage(order, CanEdit));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork, Permissions.LabValidate)]
        public async Task<IActionResult> Save(LabSaveInput input, CancellationToken ct)
        {
            var outcome = await _work.SaveResultsAsync(input, User.Can(Permissions.LabValidate), ct);
            if (outcome == null) return NotFound();
            if (outcome.Forbidden) return Forbid();
            var r = outcome.Result;
            if (!r.Ok && outcome.Order != null)
            {
                // Show what was typed; nothing was saved.
                ViewBag.Errors = r.Errors.Select(e => e.Message).ToList();
                ViewBag.SendSms = input.SendSms;
                return View("Order", _queries.OrderPage(outcome.Order, CanEdit));
            }
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Order), new { id = input.OrderId });
        }

        /// <summary>Re-opens a validated order for correction (logged).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabValidate)]
        public async Task<IActionResult> Reopen(int id, string? reason, CancellationToken ct)
        {
            var r = await _work.ReopenAsync(id, reason, ct);
            if (r == null) return NotFound();
            if (r.Message != null || !r.Ok) TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Order), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork)]
        public async Task<IActionResult> Cancel(int id, string? reason, CancellationToken ct)
        {
            var r = await _work.CancelAsync(id, reason, ct);
            if (r == null) return NotFound();
            if (!r.Ok)
            {
                TempData["Error"] = r.ErrorText;
                return RedirectToAction(nameof(Order), new { id });
            }
            TempData["Success"] = r.Message;
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Records the patient's sex (needed for some reference ranges).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork)]
        public async Task<IActionResult> SetGender(int id, Gender gender, CancellationToken ct) =>
            await _work.SetGenderAsync(id, gender, ct) ? RedirectToAction(nameof(Order), new { id }) : NotFound();

        /// <summary>Barcode sticker(s) for the tube(s). ids = comma separated order ids.</summary>
        public async Task<IActionResult> Label(string ids, CancellationToken ct)
        {
            var orders = await _queries.LabelsAsync(ids, ct);
            return orders.Count == 0 ? NotFound() : View(orders);
        }

        /// <summary>A4 result report (print / save as PDF).</summary>
        public async Task<IActionResult> Report(int id, CancellationToken ct)
        {
            var order = await _queries.OrderAsync(id, ct);
            if (order == null) return NotFound();
            if (order.Status != LabOrderStatus.Validated)
            {
                TempData["Error"] = "Nəticə blankı yalnız təsdiqdən sonra çap olunur";
                return RedirectToAction(nameof(Order), new { id });
            }
            _work.ReportPrinted(order);
            return View(_queries.OrderPage(order, CanEdit));
        }
    }
}
