using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.CatalogManage, Permissions.ReservationsView)]
    public class DoctorController : Controller
    {
        private readonly Doctors _doctors;
        public DoctorController(Doctors doctors) => _doctors = doctors;

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var (doctors, counts) = await _doctors.ListAsync(ct);
            ViewBag.TodayCounts = counts;
            return View(doctors);
        }

        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> Create(CancellationToken ct) => await Form(_doctors.NewForm(), null, ct);

        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> Edit(int id, CancellationToken ct) =>
            await _doctors.DoctorAsync(id, ct) is { } d ? await Form(_doctors.EditForm(d), d, ct) : NotFound();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> Save(DoctorFormViewModel model, CancellationToken ct)
        {
            var formErrors = ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList();
            var r = await _doctors.SaveAsync(model, formErrors, ct);
            if (r == null) return NotFound();
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Edit), new { id = r.Id });
            }
            foreach (var (field, message) in r.Errors.Where(e => !formErrors.Contains(e)))
                ModelState.AddModelError(field ?? "", message);
            return await Form(model, model.Id > 0 ? await _doctors.DoctorAsync(model.Id, ct) : null, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> AddLeave(LeaveInput input, CancellationToken ct) =>
            await _doctors.AddLeaveAsync(input, ModelState.IsValid, ct) is { } r ? Back(r, input.DoctorId) : NotFound();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> DeleteLeave(int id, CancellationToken ct) =>
            await _doctors.DeleteLeaveAsync(id, ct) is { } r ? Back(r, r.Id!.Value) : NotFound();

        private async Task<IActionResult> Form(DoctorFormViewModel model, Doctor? doctor, CancellationToken ct)
        {
            var lists = await _doctors.FormListsAsync(doctor, ct);
            ViewBag.Departments = lists.Departments;
            ViewBag.Rooms = lists.Rooms;
            ViewBag.Users = lists.Users;
            ViewBag.Leaves = lists.Leaves;
            return View("Form", model);
        }

        private IActionResult Back(Result r, int doctorId)
        {
            if (r.Ok)
            {
                if (r.Warning != null) TempData["Error"] = r.Warning;
                else TempData["Success"] = r.Message;
            }
            else TempData["Error"] = r.ErrorText;
            return RedirectToAction(nameof(Edit), new { id = doctorId });
        }
    }
}
