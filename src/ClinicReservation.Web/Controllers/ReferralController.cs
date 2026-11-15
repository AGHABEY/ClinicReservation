using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Göndərişlər: reception's list of referrals to book, and outside referrals entered by hand.</summary>
    [HasPermission(Permissions.ReservationsView)]
    public class ReferralController : Controller
    {
        private readonly ReferralQueries _queries;
        private readonly Referrals _referrals;

        public ReferralController(ReferralQueries queries, Referrals referrals)
        {
            _queries = queries;
            _referrals = referrals;
        }

        public async Task<IActionResult> Index(ReferralState? state, int? departmentId, string? q, CancellationToken ct)
        {
            var (departments, _, _) = await _queries.FormListsAsync(ct);
            ViewBag.Departments = departments;
            ViewBag.State = state ?? ReferralState.Waiting;
            ViewBag.DepartmentId = departmentId;
            ViewBag.Query = q;
            return View(await _queries.ListAsync(state, departmentId, q, ct));
        }

        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Add(string? pin, CancellationToken ct) =>
            await FormView(new ReferralFormViewModel { PinCode = pin }, ct);

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Add(ReferralFormViewModel model, CancellationToken ct)
        {
            model.FromReserveId = null; // reception enters outside referrals only
            var r = await _referrals.CreateAsync(model, ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList(), ct);
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Index));
            }
            foreach (var (field, message) in r.Errors)
                if (!(ModelState.TryGetValue(field ?? "", out var e) && e.Errors.Any(x => x.ErrorMessage == message)))
                    ModelState.AddModelError(field ?? "", message);
            return await FormView(model, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage, Permissions.RecordsEdit)]
        public async Task<IActionResult> Cancel(int id, string? reason, string? returnUrl, CancellationToken ct)
        {
            var r = await _referrals.CancelAsync(id, reason, ct);
            if (r == null) return NotFound();
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction(nameof(Index));
        }

        /// <summary>The slip the patient takes (A5-sized, prints on A4).</summary>
        [HasPermission(Permissions.ReservationsView, Permissions.RecordsView)]
        public async Task<IActionResult> Print(int id, CancellationToken ct) =>
            await _queries.ReferralAsync(id, ct) is { } r ? View(r) : NotFound();

        private async Task<IActionResult> FormView(ReferralFormViewModel model, CancellationToken ct)
        {
            var (departments, doctors, exams) = await _queries.FormListsAsync(ct);
            ViewBag.Departments = departments;
            ViewBag.Doctors = doctors;
            ViewBag.Exams = exams;
            return View("Add", model);
        }
    }
}
