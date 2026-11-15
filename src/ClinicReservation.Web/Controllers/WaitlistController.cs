using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Gözləmə siyahısı: patients waiting for an earlier time.</summary>
    [HasPermission(Permissions.ReservationsView)]
    public class WaitlistController : Controller
    {
        private readonly WaitlistQueries _queries;
        private readonly Waitlist _waitlist;

        public WaitlistController(WaitlistQueries queries, Waitlist waitlist)
        {
            _queries = queries;
            _waitlist = waitlist;
        }

        public async Task<IActionResult> Index(WaitlistStatus? status, int? doctorId, int? departmentId, CancellationToken ct)
        {
            var (doctors, departments, _) = await _queries.ListsAsync(ct);
            ViewBag.Doctors = doctors;
            ViewBag.Departments = departments;
            ViewBag.Status = status;
            ViewBag.DoctorId = doctorId;
            ViewBag.DepartmentId = departmentId;
            return View(await _queries.ListAsync(status, doctorId, departmentId, ct));
        }

        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Add(int? doctorId, int? departmentId, int? examTypeId, DateTime? date, string? pin, CancellationToken ct)
        {
            var from = date?.Date ?? ClinicTime.Today;
            if (from < ClinicTime.Today) from = ClinicTime.Today;
            var model = new WaitlistFormViewModel
            {
                DoctorId = doctorId, DepartmentId = departmentId, ExaminationTypeId = examTypeId,
                FromDate = from, ToDate = from.AddDays(14), PinCode = pin
            };
            return await FormView(model, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Add(WaitlistFormViewModel model, CancellationToken ct)
        {
            var r = await _waitlist.AddAsync(model, ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList(), ct);
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Index));
            }
            foreach (var (field, message) in r.Errors)
            {
                var key = field ?? "";
                if (ModelState.TryGetValue(key, out var entry) && entry.Errors.Any(e => e.ErrorMessage == message)) continue;
                ModelState.AddModelError(key, message);
            }
            return await FormView(model, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Decline(int id, CancellationToken ct) => Done(await _waitlist.DeclineAsync(id, ct));

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Close(int id, string? note, CancellationToken ct) => Done(await _waitlist.CloseAsync(id, note, ct));

        private IActionResult Done(Result? r)
        {
            if (r == null) return NotFound();
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Index));
        }

        private async Task<IActionResult> FormView(WaitlistFormViewModel model, CancellationToken ct)
        {
            var (doctors, departments, exams) = await _queries.ListsAsync(ct);
            model.ExistingPatient = await _queries.PatientByPinAsync(model.PinCode, ct);
            ViewBag.Doctors = doctors;
            ViewBag.Departments = departments;
            ViewBag.Exams = exams;
            return View("Add", model);
        }
    }
}
