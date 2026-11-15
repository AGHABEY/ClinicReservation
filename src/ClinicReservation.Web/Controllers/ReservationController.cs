using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Lab;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Modules.Scheduling;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.ReservationsView)]
    public class ReservationController : Controller
    {
        private readonly ReservationQueries _queries;
        private readonly Reservations _reservations;

        public ReservationController(ReservationQueries queries, Reservations reservations)
        {
            _queries = queries;
            _reservations = reservations;
        }

        // period: today | tomorrow | week | upcoming (default) | past | all
        public async Task<IActionResult> Index(string? period, string? status, int? doctorId, string? q, CancellationToken ct)
        {
            period = string.IsNullOrEmpty(period) ? "upcoming" : period;
            // A doctor's own login starts on "my appointments"; choosing "all doctors" sends an empty doctorId.
            if (doctorId == null && !Request.Query.ContainsKey("doctorId"))
                doctorId = await _queries.DoctorIdForUserAsync(User.FindFirstValue(ClaimTypes.NameIdentifier), ct);

            ViewBag.Period = period;
            ViewBag.Status = status;
            ViewBag.DoctorId = doctorId;
            ViewBag.Query = q;
            ViewBag.Doctors = await _queries.AllDoctorsAsync(ct);
            return View(await _queries.ListAsync(period, status, doctorId, q, ct));
        }

        // ---------- Create ----------

        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Add(DateTime? date, int? doctorId, int? patientId, CancellationToken ct)
        {
            var model = new ReserveFormViewModel { AppointmentDate = (date ?? ClinicTime.Today).Date, DoctorId = doctorId };
            if (patientId != null && await _queries.PatientAsync(patientId.Value, ct) is { } p)
            {
                model.PinCode = p.PinCode;
                model.Name = p.Name;
                model.Surname = p.Surname;
                model.Phone = p.Phone;
                model.Birthday = p.Birthday;
                model.ExistingPatient = p;
            }
            return await FormView(model, null, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Add(ReserveFormViewModel model, CancellationToken ct)
        {
            model.Id = 0;
            var patient = await _queries.PatientByPinAsync(model.PinCode, ct);
            // An existing card is the source of truth for name/birthday; only the phone may be refreshed.
            if (patient != null)
            {
                foreach (var key in new[] { nameof(model.Name), nameof(model.Surname), nameof(model.Birthday) })
                    ModelState.Remove(key);
                if (string.IsNullOrWhiteSpace(model.Phone)) ModelState.Remove(nameof(model.Phone));
            }

            var r = await _reservations.CreateAsync(model, patient, FormErrors(), ct);
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Details), new { id = r.Id });
            }
            ShowErrors(r);
            model.ExistingPatient = patient;
            return await FormView(model, null, ct);
        }

        // ---------- Edit ----------

        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Edit(int id, CancellationToken ct)
        {
            var reserve = await _queries.ReserveAsync(id, ct);
            if (reserve == null) return NotFound();
            if (!ReserveFlow.CanEdit(reserve)) return NotEditable(reserve);

            var model = new ReserveFormViewModel
            {
                Id = reserve.Id,
                PatientId = reserve.PatientId,
                PinCode = reserve.Patient.PinCode,
                Name = reserve.Patient.Name,
                Surname = reserve.Patient.Surname,
                Phone = reserve.Patient.Phone,
                Birthday = reserve.Patient.Birthday,
                DoctorId = reserve.DoctorId,
                AppointmentDate = reserve.AppointmentAt.Date,
                AppointmentTime = reserve.AppointmentAt.ToString("HH:mm"),
                ExaminationTypeIds = reserve.ReserveExaminationTypes!.Select(x => x.ExaminationTypeId).ToList(),
                ExistingPatient = reserve.Patient
            };
            return await FormView(model, reserve, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Edit(ReserveFormViewModel model, CancellationToken ct)
        {
            var reserve = await _queries.ReserveAsync(model.Id, ct);
            if (reserve == null) return NotFound();
            if (!ReserveFlow.CanEdit(reserve)) return NotEditable(reserve);

            // The patient is fixed on an existing reservation (edit the card itself instead).
            foreach (var key in new[] { nameof(model.PinCode), nameof(model.Name), nameof(model.Surname), nameof(model.Phone), nameof(model.Birthday) })
                ModelState.Remove(key);
            // After check-in only the examinations may change.
            if (!ReserveFlow.CanReschedule(reserve))
            {
                model.DoctorId = reserve.DoctorId;
                model.AppointmentDate = reserve.AppointmentAt.Date;
                model.AppointmentTime = reserve.AppointmentAt.ToString("HH:mm");
                foreach (var key in new[] { nameof(model.DoctorId), nameof(model.AppointmentDate), nameof(model.AppointmentTime) })
                    ModelState.Remove(key);
            }

            var r = await _reservations.UpdateAsync(reserve, model, FormErrors(), ct);
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Details), new { id = reserve.Id });
            }
            ShowErrors(r);
            model.ExistingPatient = reserve.Patient;
            return await FormView(model, reserve, ct);
        }

        // ---------- Status changes ----------

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage, Permissions.ReservationsStatus)]
        public async Task<IActionResult> Transition(int id, string to, string? returnTo, CancellationToken ct)
        {
            var outcome = await _reservations.TransitionAsync(id, to, User, ct);
            if (outcome == null) return NotFound();
            var (r, doctorId) = outcome.Value;
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                if (r.Warning != null) TempData["Error"] = r.Warning;
            }
            else TempData["Error"] = r.ErrorText;

            return returnTo switch
            {
                "details" => RedirectToAction(nameof(Details), new { id }),
                "home" => RedirectToAction("Index", "Home"),
                "workspace" => RedirectToAction("Index", "Workspace", new { doctorId }),
                "record" when to == StatusNames.InProgress => RedirectToAction("Edit", "Record", new { reserveId = id }),
                _ => RedirectToAction(nameof(Index), new { period = Request.Form["period"].ToString() })
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public async Task<IActionResult> Cancel(CancelReserveViewModel model, CancellationToken ct)
        {
            var r = await _reservations.CancelAsync(model.Id, model.ReasonId, model.By, model.Reason, ct);
            if (r == null) return NotFound();
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        public async Task<IActionResult> Details(int id, [FromServices] LabQueries lab, [FromServices] StockQueries stock, CancellationToken ct)
        {
            var res = await _queries.ReserveAsync(id, ct);
            if (res == null) return NotFound();
            ViewBag.CancelReasons = await _queries.CancellationReasonsAsync(ct);
            if (User.CanAny(Permissions.StockView, Permissions.StockIssue, Permissions.StockManage))
            {
                ViewBag.StockDocs = await stock.ForReserveAsync(id, ct);
                // Completed, but some service's materials were never taken (e.g. stock failed at that moment).
                ViewBag.StockPending = res.Status?.Name == StatusNames.Completed && await stock.HasPendingMaterialsAsync(id, ct);
            }
            if (User.CanAny(Permissions.LabView, Permissions.LabWork))
            {
                ViewBag.LabOrders = await lab.OrdersForReserveAsync(id, ct);
                ViewBag.LabPending = lab.PendingTests(id);
            }
            return View(res);
        }

        /// <summary>JSON slots for the reservation form (length and device follow the chosen services).</summary>
        [HttpGet]
        public IActionResult Slots(int doctorId, DateTime date, int? exclude, [FromQuery] List<int>? examTypeIds)
        {
            var (result, shape) = _queries.Slots(doctorId, date, exclude, examTypeIds);
            return Json(new
            {
                message = result.Message,
                minutes = result.SlotMinutes,
                equipment = shape.EquipmentName,
                slots = result.Slots.Select(s => new { time = s.Time, past = s.Past, available = s.Available })
            });
        }

        // ---------- Helpers ----------

        private async Task<IActionResult> FormView(ReserveFormViewModel model, Reserve? existing, CancellationToken ct)
        {
            var data = await _queries.FormDataAsync(model, existing, ct);
            ViewBag.ExaminationTypes = data.ExaminationTypes;
            ViewBag.Doctors = data.Doctors;
            ViewBag.Slots = data.Slots;
            ViewBag.LockedPrices = data.LockedPrices;
            ViewBag.Shape = data.Shape;
            return View(model);
        }

        private IActionResult NotEditable(Reserve reserve)
        {
            TempData["Error"] = $"«{StatusNames.Label(reserve.Status?.Name)}» statusunda rezervasiyaya düzəliş etmək olmaz";
            return RedirectToAction(nameof(Details), new { id = reserve.Id });
        }

        /// <summary>The form's own errors (DataAnnotations), handed to the use case so all problems show together.</summary>
        private List<(string? Field, string Message)> FormErrors() =>
            ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList();

        /// <summary>Adds the use case's own errors (the form's are already in ModelState, with what was typed).</summary>
        private void ShowErrors(Result r)
        {
            foreach (var (field, message) in r.Errors)
            {
                var key = field ?? "";
                if (ModelState.TryGetValue(key, out var entry) && entry.Errors.Any(e => e.ErrorMessage == message)) continue;
                ModelState.AddModelError(key, message);
            }
        }
    }
}
