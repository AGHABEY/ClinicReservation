using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Emr;
using ClinicReservation.Modules.Inpatient;
using ClinicReservation.Modules.Lab;
using ClinicReservation.Modules.Patients;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.PatientsView)]
    public class PatientController : Controller
    {
        private readonly PatientDirectory _patients;
        public PatientController(PatientDirectory patients) => _patients = patients;

        public async Task<IActionResult> Index(string? q, CancellationToken ct, int page = 1)
        {
            page = Math.Max(1, page);
            var (rows, total) = await _patients.SearchAsync(q, page, ct);
            ViewBag.Query = q;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.PageSize = PatientDirectory.PageSize;
            return View(rows);
        }

        /// <summary>The card: visits, plus notes, samples and stays from the modules the user may see.</summary>
        public async Task<IActionResult> Details(int id, [FromServices] MedicalRecords records, [FromServices] LabQueries lab,
            [FromServices] InpatientQueries inpatient, CancellationToken ct)
        {
            var patient = await _patients.CardAsync(id, ct);
            if (patient == null) return NotFound();
            if (User.Can(Permissions.RecordsView)) ViewBag.Records = await records.ForPatientAsync(id, ct);
            if (User.Can(Permissions.LabView)) ViewBag.LabOrders = await lab.ForPatientAsync(id, ct);
            if (User.CanAny(Permissions.InpatientView, Permissions.InpatientManage, Permissions.InpatientDoctor, Permissions.InpatientNurse))
                ViewBag.Admissions = await inpatient.ForPatientAsync(id, ct);
            return View(patient);
        }

        /// <summary>FİN lookup for the reservation form.</summary>
        [HttpGet]
        public async Task<IActionResult> Lookup(string pin, CancellationToken ct)
        {
            var found = await _patients.LookupAsync(pin, ct);
            if (found == null) return Json(new { found = false });
            var (p, visits) = found.Value;
            return Json(new
            {
                found = true,
                id = p.Id,
                name = p.Name,
                surname = p.Surname,
                phone = p.Phone,
                birthday = p.Birthday.ToString("yyyy-MM-dd"),
                visits,
                url = Url.Action(nameof(Details), new { id = p.Id })
            });
        }

        [HasPermission(Permissions.PatientsEdit)]
        public IActionResult Create() => View("Form", new PatientFormViewModel());

        [HasPermission(Permissions.PatientsEdit)]
        public async Task<IActionResult> Edit(int id, CancellationToken ct) =>
            await _patients.FormAsync(id, ct) is { } model ? View("Form", model) : NotFound();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.PatientsEdit)]
        public async Task<IActionResult> Save(PatientFormViewModel model, CancellationToken ct)
        {
            var formErrors = ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList();
            var r = await _patients.SaveAsync(model, formErrors, ct);
            if (r == null) return NotFound();
            if (!r.Ok)
            {
                foreach (var (field, message) in r.Errors.Where(e => !formErrors.Contains(e)))
                    ModelState.AddModelError(field ?? "", message);
                return View("Form", model);
            }
            TempData["Success"] = r.Message;
            return RedirectToAction(nameof(Details), new { id = r.Id });
        }
    }
}
