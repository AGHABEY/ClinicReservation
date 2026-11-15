using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Emr;
using ClinicReservation.Modules.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Elektron tibbi kart: the doctor's note for a visit.</summary>
    [HasPermission(Permissions.RecordsView, Permissions.RecordsEdit)]
    public class RecordController : Controller
    {
        private readonly MedicalRecords _records;
        private readonly ReferralQueries _referralQueries;

        public RecordController(MedicalRecords records, ReferralQueries referralQueries)
        {
            _records = records;
            _referralQueries = referralQueries;
        }

        private bool IsAdmin => User.IsInRole(AppRoles.Admin);

        [HasPermission(Permissions.RecordsEdit)]
        public async Task<IActionResult> Edit(int reserveId, CancellationToken ct)
        {
            var reserve = await _records.VisitAsync(reserveId, ct);
            if (reserve == null) return NotFound();
            if (await Denied(reserve, ct) is { } denied) return denied;
            await ReferralBag(reserve.Id, ct);
            return View(await _records.PrepareAsync(MedicalRecords.Form(reserve), reserve, ct));
        }

        /// <summary>The visit's referrals and the lists for writing one (Scheduling owns referrals).</summary>
        private async Task ReferralBag(int reserveId, CancellationToken ct)
        {
            var q = _referralQueries;
            ViewBag.Referrals = await q.FromVisitAsync(reserveId, ct);
            var (departments, doctors, exams) = await q.FormListsAsync(ct);
            ViewBag.RefDepartments = departments;
            ViewBag.RefDoctors = doctors;
            ViewBag.RefExams = exams;
        }

        /// <summary>The doctor refers the patient to another specialist / department / service.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.RecordsEdit)]
        public async Task<IActionResult> Refer(ReferralFormViewModel model, [FromServices] Referrals referrals, CancellationToken ct)
        {
            var reserve = await _records.VisitAsync(model.FromReserveId ?? 0, ct);
            if (reserve == null) return NotFound();
            if (await Denied(reserve, ct) is { } denied) return denied;
            var formErrors = ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList();
            var r = await referrals.CreateAsync(model, formErrors, ct);
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Edit), new { reserveId = reserve.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.RecordsEdit)]
        public async Task<IActionResult> Edit(RecordFormViewModel model, CancellationToken ct)
        {
            var reserve = await _records.VisitAsync(model.ReserveId, ct);
            if (reserve == null) return NotFound();
            if (await Denied(reserve, ct) is { } denied) return denied;

            var formErrors = ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList();
            var r = await _records.SaveAsync(reserve, model, formErrors, ct);
            if (!r.Ok)
            {
                foreach (var (field, message) in r.Errors.Where(e => !formErrors.Contains(e)))
                    ModelState.AddModelError(field ?? "", message);
                model.Exists = reserve.MedicalRecord != null;
                await ReferralBag(reserve.Id, ct);
                return View(await _records.PrepareAsync(model, reserve, ct));
            }
            TempData["Success"] = r.Message;
            if (r.Warning != null) TempData["Error"] = r.Warning;
            return model.Finish == "1"
                ? RedirectToAction(nameof(Show), new { id = r.Id })
                : RedirectToAction(nameof(Edit), new { reserveId = reserve.Id });
        }

        [HasPermission(Permissions.RecordsView)]
        public async Task<IActionResult> Show(int id, CancellationToken ct)
        {
            var rec = await _records.RecordAsync(id, ct);
            if (rec == null) return NotFound();
            _records.Viewed(rec);
            ViewBag.CanWrite = User.Can(Permissions.RecordsEdit) && await _records.CanWriteAsync(rec.Reserve, IsAdmin, ct) == RecordAccess.Allowed;
            return View(rec);
        }

        [HasPermission(Permissions.RecordsView)]
        public async Task<IActionResult> Print(int id, CancellationToken ct)
        {
            var rec = await _records.RecordAsync(id, ct);
            if (rec == null) return NotFound();
            _records.Printed(rec);
            return View(rec);
        }

        /// <summary>
        /// The doctor orders lab examinations during the visit: they are added to the
        /// reservation (price locked now, paid at the cashier) and appear in the lab's list.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.RecordsEdit)]
        public async Task<IActionResult> OrderLab(int reserveId, List<int>? examinationTypeIds, CancellationToken ct)
        {
            var reserve = await _records.VisitAsync(reserveId, ct);
            if (reserve == null) return NotFound();
            if (await Denied(reserve, ct) is { } denied) return denied;
            var r = await _records.OrderLabAsync(reserve, examinationTypeIds, ct);
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Edit), new { reserveId });
        }

        private async Task<IActionResult?> Denied(Reserve reserve, CancellationToken ct)
        {
            var access = await _records.CanWriteAsync(reserve, IsAdmin, ct);
            if (access == RecordAccess.Allowed) return null;
            TempData["Error"] = MedicalRecords.Message(access);
            return RedirectToAction("Details", "Reservation", new { id = reserve.Id });
        }
    }
}
