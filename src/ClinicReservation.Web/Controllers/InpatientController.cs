using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Inpatient;
using ClinicReservation.Web;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Stasionar: bed board, admission, daily notes, medication orders and round, discharge.</summary>
    [HasPermission(Permissions.InpatientView, Permissions.InpatientManage, Permissions.InpatientDoctor, Permissions.InpatientNurse)]
    public class InpatientController : Controller
    {
        private readonly InpatientQueries _queries;
        private readonly InpatientCare _care;

        public InpatientController(InpatientQueries queries, InpatientCare care)
        {
            _queries = queries;
            _care = care;
        }

        // ---------- Board and lists ----------

        public async Task<IActionResult> Index(int? departmentId, CancellationToken ct) =>
            View(await _queries.BoardAsync(departmentId, ct));

        public async Task<IActionResult> List(string? status, DateTime? from, DateTime? to, string? q, CancellationToken ct) =>
            View(await _queries.ListAsync(status, from, to, q, ct));

        // ---------- Admission ----------

        [HasPermission(Permissions.InpatientManage, Permissions.InpatientDoctor)]
        public async Task<IActionResult> Admit(int? patientId, int? bedId, int? reserveId, CancellationToken ct) =>
            await _queries.AdmitFormAsync(patientId, bedId, reserveId, ct) is { } model ? View(model) : NotFound();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientManage, Permissions.InpatientDoctor)]
        public async Task<IActionResult> Admit(AdmitViewModel model, CancellationToken ct)
        {
            if (ModelState.IsValid)
            {
                var r = await _care.AdmitAsync(model, ct);
                if (r.Ok)
                {
                    Flash(r);
                    return RedirectToAction(nameof(Details), new { id = r.Id });
                }
                ModelState.AddErrors(r);
            }
            return View(await _queries.PrepareAdmitAsync(model, ct));
        }

        // ---------- Case page ----------

        public async Task<IActionResult> Details(int id, string? tab, DateTime? day, CancellationToken ct)
        {
            var a = await _queries.AdmissionAsync(id, ct);
            return a == null ? NotFound() : View(await CasePage(a, tab, day, ct));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientManage)]
        public async Task<IActionResult> Move(int id, int bedId, CancellationToken ct)
        {
            Flash(await _care.MoveAsync(id, bedId, ct));
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor, Permissions.InpatientNurse)]
        public async Task<IActionResult> Observe(ObservationInput input, CancellationToken ct)
        {
            var a = await _queries.AdmissionAsync(input.AdmissionId, ct);
            if (a == null) return NotFound();
            if (input.Kind == ObservationKind.Doctor && !User.Can(Permissions.InpatientDoctor)) return Forbid();
            if (!Enum.IsDefined(input.Kind)) return BadRequest();

            var r = await _care.ObserveAsync(a, input, FormErrors(), ct);
            if (!r.Ok)
            {
                var vm = await CasePage(a, "journal", null, ct);
                vm.NewObservation = input;
                vm.Errors = r.Errors.Select(e => e.Message).Distinct().ToList();
                return View(nameof(Details), vm);
            }
            Flash(r);
            return RedirectToAction(nameof(Details), new { id = a.Id, tab = "journal" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor)]
        public async Task<IActionResult> Order(OrderInput input, CancellationToken ct)
        {
            var a = await _queries.AdmissionAsync(input.AdmissionId, ct);
            if (a == null) return NotFound();
            var r = await _care.OrderAsync(a, input, FormErrors(), ct);
            if (!r.Ok)
            {
                var vm = await CasePage(a, "orders", null, ct);
                vm.NewOrder = input;
                vm.Errors = r.Errors.Select(e => e.Message).Distinct().ToList();
                return View(nameof(Details), vm);
            }
            Flash(r);
            return RedirectToAction(nameof(Details), new { id = a.Id, tab = "orders" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor)]
        public async Task<IActionResult> StopOrder(int id, int orderId, CancellationToken ct)
        {
            var r = await _care.StopOrderAsync(id, orderId, ct);
            if (r == null) return NotFound();
            Flash(r);
            return RedirectToAction(nameof(Details), new { id, tab = "orders" });
        }

        /// <summary>Medication round: a nurse marks a scheduled dose as given or not given.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientNurse)]
        public async Task<IActionResult> Dose(int id, int orderId, string? slot, DoseStatus status, string? note, DateTime? day, CancellationToken ct)
        {
            var r = await _care.RecordDoseAsync(id, orderId, slot, status, note, ct);
            if (r == null) return NotFound();
            Flash(r);
            return RedirectToAction(nameof(Details), new { id, tab = "mar", day = (day ?? ClinicTime.Today).ToString("yyyy-MM-dd") });
        }

        // ---------- Discharge ----------

        [HasPermission(Permissions.InpatientDoctor)]
        public async Task<IActionResult> Discharge(int id, CancellationToken ct)
        {
            var a = await _queries.AdmissionAsync(id, ct);
            if (a == null) return NotFound();
            if (!a.IsActive) return RedirectToAction(nameof(Epicrisis), new { id });
            ViewBag.Admission = a;
            return View(InpatientQueries.DischargeForm(a));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor)]
        public async Task<IActionResult> Discharge(DischargeInput input, CancellationToken ct)
        {
            var a = await _queries.AdmissionAsync(input.AdmissionId, ct);
            if (a == null) return NotFound();
            if (!ModelState.IsValid)
            {
                ViewBag.Admission = a;
                return View(input);
            }
            var r = _care.Discharge(a, input);
            Flash(r);
            return r.Ok ? RedirectToAction(nameof(Epicrisis), new { id = a.Id }) : RedirectToAction(nameof(Details), new { id = a.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientManage)]
        public async Task<IActionResult> Cancel(int id, string? reason, CancellationToken ct)
        {
            var a = await _queries.AdmissionAsync(id, ct);
            if (a == null) return NotFound();
            var r = _care.Cancel(a, reason);
            Flash(r);
            return r.Ok ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id });
        }

        public async Task<IActionResult> Epicrisis(int id, CancellationToken ct)
        {
            var a = await _queries.AdmissionAsync(id, ct);
            if (a == null) return NotFound();
            _care.EpicrisisViewed(a);
            return View(a);
        }

        // ------------------------------------------------------------------

        private Task<AdmissionViewModel> CasePage(Admission a, string? tab, DateTime? day, CancellationToken ct) =>
            _queries.CasePageAsync(a, tab, day, User.Can(Permissions.InpatientManage), User.Can(Permissions.InpatientDoctor), ct);

        private IEnumerable<string> FormErrors() => ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);

        /// <summary>Success → green flash (+ a warning in red if there is one); failure → red flash.</summary>
        private void Flash(Result r)
        {
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                if (r.Warning != null) TempData["Error"] = r.Warning;
            }
            else TempData["Error"] = r.ErrorText;
        }
    }
}
