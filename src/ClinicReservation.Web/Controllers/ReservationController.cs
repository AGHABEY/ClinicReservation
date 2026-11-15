using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Lab;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Services;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.ReservationsView)]
    public class ReservationController : Controller
    {
        private readonly AppDbContext _context;
        private readonly SlotService _slots;
        private readonly AuditService _audit;
        private readonly QueueService _queue;

        public ReservationController(AppDbContext context, SlotService slots, AuditService audit, QueueService queue)
        {
            _context = context;
            _slots = slots;
            _audit = audit;
            _queue = queue;
        }

        // period: today | tomorrow | week | upcoming (default) | past | all
        public IActionResult Index(string? period, string? status, int? doctorId, string? q)
        {
            period = string.IsNullOrEmpty(period) ? "upcoming" : period;
            var today = ClinicTime.Today;

            // A doctor's own login starts on "my appointments"; choosing "all doctors" sends an empty doctorId.
            var myDoctor = MyDoctorId();
            if (doctorId == null && myDoctor != null && !Request.Query.ContainsKey("doctorId"))
                doctorId = myDoctor;

            var query = _context.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.Room)
                .AsQueryable();

            query = period switch
            {
                "today" => query.Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1)),
                "tomorrow" => query.Where(r => r.AppointmentAt >= today.AddDays(1) && r.AppointmentAt < today.AddDays(2)),
                "week" => query.Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(7)),
                "past" => query.Where(r => r.AppointmentAt < today),
                "all" => query,
                _ => query.Where(r => r.AppointmentAt >= today)
            };

            if (!string.IsNullOrEmpty(status) && StatusNames.All.Contains(status))
                query = query.Where(r => r.Status!.Name == status);

            if (doctorId != null)
                query = query.Where(r => r.DoctorId == doctorId);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(r =>
                    EF.Functions.ILike(r.Patient.Name + " " + r.Patient.Surname, term) ||
                    EF.Functions.ILike(r.Patient.PinCode, term) ||
                    EF.Functions.ILike(r.Patient.Phone, term));
            }

            query = period is "past" or "all"
                ? query.OrderByDescending(r => r.AppointmentAt)
                : query.OrderBy(r => r.AppointmentAt);

            ViewBag.Period = period;
            ViewBag.Status = status;
            ViewBag.DoctorId = doctorId;
            ViewBag.Query = q;
            ViewBag.Doctors = _context.Doctors.OrderBy(d => d.FullName).ToList();
            return View(query.Take(500).ToList());
        }

        // ---------- Create ----------

        [HasPermission(Permissions.ReservationsManage)]
        public IActionResult Add(DateTime? date, int? doctorId, int? patientId)
        {
            var model = new ReserveFormViewModel
            {
                AppointmentDate = (date ?? ClinicTime.Today).Date,
                DoctorId = doctorId
            };

            if (patientId != null)
            {
                var p = _context.Patients.Find(patientId);
                if (p != null)
                {
                    model.PinCode = p.PinCode;
                    model.Name = p.Name;
                    model.Surname = p.Surname;
                    model.Phone = p.Phone;
                    model.Birthday = p.Birthday;
                    model.ExistingPatient = p;
                }
            }

            PrepareForm(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public IActionResult Add(ReserveFormViewModel model)
        {
            model.Id = 0;
            var pin = Patient.NormalizePin(model.PinCode);
            var patient = string.IsNullOrEmpty(pin) ? null : _context.Patients.FirstOrDefault(p => p.PinCode == pin);

            // An existing card is the source of truth for name/birthday; only the phone may be refreshed.
            if (patient != null)
            {
                foreach (var key in new[] { nameof(model.Name), nameof(model.Surname), nameof(model.Birthday) })
                    ModelState.Remove(key);
                if (string.IsNullOrWhiteSpace(model.Phone))
                    ModelState.Remove(nameof(model.Phone));
            }

            var examTypes = ValidateForm(model, existing: null, out var start, out var slot);

            if (!ModelState.IsValid)
            {
                model.ExistingPatient = patient;
                PrepareForm(model);
                return View(model);
            }

            var createdPatient = false;
            if (patient == null)
            {
                patient = new Patient
                {
                    PinCode = pin,
                    Name = model.Name!.Trim(),
                    Surname = model.Surname!.Trim(),
                    Phone = model.Phone!.Trim(),
                    Birthday = model.Birthday!.Value.Date,
                    CreatedAt = ClinicTime.Now
                };
                _context.Patients.Add(patient);
                createdPatient = true;
            }
            else if (!string.IsNullOrWhiteSpace(model.Phone) && model.Phone.Trim() != patient.Phone)
            {
                patient.Phone = model.Phone.Trim();
            }

            var reserve = new Reserve
            {
                Patient = patient,
                DoctorId = model.DoctorId,
                RoomId = slot!.RoomId,
                AppointmentAt = start,
                DurationMinutes = slot.SlotMinutes,
                AddedDate = ClinicTime.Now,
                Status = _context.ReserveStatuses.First(rs => rs.Name == StatusNames.Waiting),
                ReserveExaminationTypes = examTypes
                    .Select(e => new ReserveExaminationType { ExaminationTypeId = e.Id, Price = e.Price })
                    .ToList()
            };

            _context.Reserves.Add(reserve);
            _context.SaveChanges();

            if (createdPatient)
                _audit.Log("Xəstə kartı yaradıldı", AuditEntities.Patient, patient.Id, $"{patient.FullName} ({patient.PinCode})");
            var doctorName = _context.Doctors.Where(d => d.Id == reserve.DoctorId).Select(d => d.FullName).FirstOrDefault();
            _audit.Log("Rezervasiya yaradıldı", AuditEntities.Reservation, reserve.Id,
                $"{patient.FullName} — {Fmt.DateTime(reserve.AppointmentAt)}, {doctorName}");

            TempData["Success"] = $"{patient.FullName} — {Fmt.DateTime(reserve.AppointmentAt)} üçün rezervasiya yaradıldı";
            return RedirectToAction(nameof(Details), new { id = reserve.Id });
        }

        // ---------- Edit ----------

        [HasPermission(Permissions.ReservationsManage)]
        public IActionResult Edit(int id)
        {
            var reserve = LoadReserve(id);
            if (reserve == null) return NotFound();
            if (!ReserveFlow.CanEdit(reserve))
            {
                TempData["Error"] = $"«{StatusNames.Label(reserve.Status?.Name)}» statusunda rezervasiyaya düzəliş etmək olmaz";
                return RedirectToAction(nameof(Details), new { id });
            }

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
            PrepareForm(model, reserve);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public IActionResult Edit(ReserveFormViewModel model)
        {
            var reserve = LoadReserve(model.Id);
            if (reserve == null) return NotFound();
            if (!ReserveFlow.CanEdit(reserve))
            {
                TempData["Error"] = $"«{StatusNames.Label(reserve.Status?.Name)}» statusunda rezervasiyaya düzəliş etmək olmaz";
                return RedirectToAction(nameof(Details), new { id = reserve.Id });
            }

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

            var examTypes = ValidateForm(model, reserve, out var start, out var slot);
            if (!ModelState.IsValid)
            {
                model.ExistingPatient = reserve.Patient;
                PrepareForm(model, reserve);
                return View(model);
            }

            var changes = new List<string>();
            if (reserve.AppointmentAt != start || reserve.DoctorId != model.DoctorId)
            {
                changes.Add($"vaxt {Fmt.DateTime(reserve.AppointmentAt)} → {Fmt.DateTime(start)}");
                reserve.AppointmentAt = start;
                if (reserve.DoctorId != model.DoctorId)
                    changes.Add("həkim dəyişdi");
                reserve.DoctorId = model.DoctorId;
                if (slot != null)
                {
                    reserve.RoomId = slot.RoomId;
                    reserve.DurationMinutes = slot.SlotMinutes;
                }
            }

            // Keep the locked price of examinations that stay; new ones get today's price.
            var selectedIds = examTypes.Select(e => e.Id).ToHashSet();
            var items = reserve.ReserveExaminationTypes!;
            var removed = items.Where(i => !selectedIds.Contains(i.ExaminationTypeId)).ToList();
            _context.ReserveExaminationTypes.RemoveRange(removed);
            var existingIds = items.Select(i => i.ExaminationTypeId).ToHashSet();
            var added = examTypes.Where(e => !existingIds.Contains(e.Id)).ToList();
            foreach (var e in added)
                items.Add(new ReserveExaminationType { ExaminationTypeId = e.Id, Price = e.Price });
            if (removed.Count > 0) changes.Add("çıxarıldı: " + string.Join(", ", removed.Select(r => r.ExaminationType.Name)));
            if (added.Count > 0) changes.Add("əlavə edildi: " + string.Join(", ", added.Select(a => a.Name)));

            _context.SaveChanges();
            _audit.Log("Rezervasiya dəyişdirildi", AuditEntities.Reservation, reserve.Id,
                $"{reserve.Patient.FullName}: " + (changes.Count > 0 ? string.Join("; ", changes) : "dəyişiklik yoxdur"));

            TempData["Success"] = "Rezervasiya yeniləndi";
            return RedirectToAction(nameof(Details), new { id = reserve.Id });
        }

        // ---------- Status changes ----------

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage, Permissions.ReservationsStatus)]
        public IActionResult Transition(int id, string to, string? returnTo, [FromServices] StockService stock)
        {
            var res = _context.Reserves.Include(r => r.Status).Include(r => r.Patient).FirstOrDefault(rs => rs.Id == id);
            if (res == null)
                return NotFound();

            var now = ClinicTime.Now;
            if (!ReserveFlow.IsAllowed(res, to, User, now))
            {
                TempData["Error"] = $"«{StatusNames.Label(res.Status?.Name)}» statusundan «{StatusNames.Label(to)}» statusuna keçmək olmaz";
            }
            else
            {
                var from = res.Status?.Name;
                res.Status = _context.ReserveStatuses.First(rs => rs.Name == to);
                switch (to)
                {
                    case StatusNames.Confirmed: res.ConfirmDate = now; break;
                    case StatusNames.Arrived:
                        res.ArrivedAt = now;
                        _queue.AssignTicket(res, now);
                        break;
                    case StatusNames.InProgress: res.StartedAt = now; res.ArrivedAt ??= now; break;
                    case StatusNames.Completed: res.CompletedAt = now; res.StartedAt ??= now; res.ArrivedAt ??= now; break;
                }
                _context.SaveChanges();
                _audit.Log("Status dəyişdi", AuditEntities.Reservation, res.Id,
                    $"{res.Patient.FullName}: {StatusNames.Label(from)} → {StatusNames.Label(to)}"
                    + (to == StatusNames.Arrived ? $", talon {res.QueueTicket}" : ""));
                TempData["Success"] = to == StatusNames.Arrived
                    ? $"{res.Patient.FullName} — gəldi. Növbə talonu: {res.QueueTicket}"
                    : $"{res.Patient.FullName} — {StatusNames.Label(to)}";
                if (to == StatusNames.Completed)
                {
                    var warn = StockService.Warning(stock.ConsumeForReserve(res.Id, false, User.Identity?.Name));
                    if (warn != null) TempData["Error"] = warn;
                }
            }

            return returnTo switch
            {
                "details" => RedirectToAction(nameof(Details), new { id }),
                "home" => RedirectToAction("Index", "Home"),
                "workspace" => RedirectToAction("Index", "Workspace", new { doctorId = res.DoctorId }),
                "record" when to == StatusNames.InProgress => RedirectToAction("Edit", "Record", new { reserveId = id }),
                _ => RedirectToAction(nameof(Index), new { period = Request.Form["period"].ToString() })
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.ReservationsManage)]
        public IActionResult Cancel(CancelReserveViewModel model)
        {
            var res = LoadReserve(model.Id);
            if (res == null)
                return NotFound();

            if (!ReserveFlow.CanCancel(res))
            {
                TempData["Error"] = $"«{StatusNames.Label(res.Status?.Name)}» statusunda rezervasiyanı ləğv etmək olmaz";
                return RedirectToAction(nameof(Details), new { id = res.Id });
            }

            res.CancelDate = ClinicTime.Now;
            res.CancelReason = string.IsNullOrWhiteSpace(model.Reason) ? null : model.Reason.Trim();
            res.Status = _context.ReserveStatuses.First(rs => rs.Name == StatusNames.Cancelled);
            _context.SaveChanges();
            _audit.Log("Rezervasiya ləğv edildi", AuditEntities.Reservation, res.Id,
                $"{res.Patient.FullName}" + (res.CancelReason == null ? "" : $" — səbəb: {res.CancelReason}"));

            TempData["Success"] = res.Paid > 0
                ? $"Rezervasiya ləğv edildi. Xəstəyə {Fmt.Money(res.Paid)} qaytarılmalıdır — Kassada qeyd edin."
                : "Rezervasiya ləğv edildi";
            return RedirectToAction(nameof(Details), new { id = res.Id });
        }

        public IActionResult Details(int id, [FromServices] LabService lab)
        {
            var res = LoadReserve(id);
            if (res == null)
                return NotFound();
            if (User.CanAny(Permissions.StockView, Permissions.StockIssue, Permissions.StockManage))
            {
                ViewBag.StockDocs = _context.StockDocuments.Include(d => d.Movements).ThenInclude(m => m.StockItem)
                    .Include(d => d.FromWarehouse)
                    .Where(d => d.ReserveId == id).OrderBy(d => d.CreatedAt).ToList();
                // Completed, but some service's materials were never taken (e.g. stock failed at that moment).
                ViewBag.StockPending = res.Status?.Name == StatusNames.Completed
                    && _context.ReserveExaminationTypes.Any(x => x.ReserveId == id && x.StockConsumedAt == null && x.ExaminationType.Materials.Any());
            }
            if (User.CanAny(Permissions.LabView, Permissions.LabWork))
            {
                ViewBag.LabOrders = _context.LabOrders.Include(o => o.Results).ThenInclude(r => r.LabTest)
                    .Where(o => o.ReserveId == id).OrderBy(o => o.CollectedAt).ToList();
                ViewBag.LabPending = lab.PendingTests(id);
            }
            return View(res);
        }

        /// <summary>JSON slots for the reservation form.</summary>
        [HttpGet]
        public IActionResult Slots(int doctorId, DateTime date, int? exclude)
        {
            var result = _slots.GetSlots(doctorId, date, exclude);
            return Json(new
            {
                message = result.Message,
                slots = result.Slots.Select(s => new { time = s.Time, past = s.Past, available = s.Available })
            });
        }

        // ---------- Helpers ----------

        private int? MyDoctorId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return userId == null ? null : _context.Doctors.Where(d => d.UserId == userId).Select(d => (int?)d.Id).FirstOrDefault();
        }

        private Reserve? LoadReserve(int id) =>
            _context.Reserves
                .Include(rs => rs.Status)
                .Include(rs => rs.Patient)
                .Include(rs => rs.Doctor).ThenInclude(d => d!.Department)
                .Include(rs => rs.Room)
                .Include(rs => rs.Payments)
                .Include(rs => rs.MedicalRecord)
                .Include(rs => rs.ReserveExaminationTypes!).ThenInclude(rs => rs.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefault(rs => rs.Id == id);

        private void PrepareForm(ReserveFormViewModel model, Reserve? existing = null)
        {
            ViewBag.ExaminationTypes = _context.ExaminationTypes.Include(e => e.Department)
                .OrderBy(e => e.Department == null ? "" : e.Department.Name).ThenBy(e => e.Name).ToList();

            var doctors = _context.Doctors.Include(d => d.Department)
                .Where(d => d.IsActive || (existing != null && d.Id == existing.DoctorId))
                .OrderBy(d => d.Department == null ? "" : d.Department.Name).ThenBy(d => d.FullName)
                .ToList();
            ViewBag.Doctors = doctors;

            var slots = new SlotResult();
            if (model.DoctorId != null && model.AppointmentDate.HasValue)
                slots = _slots.GetSlots(model.DoctorId.Value, model.AppointmentDate.Value, existing?.Id);

            // Keep the reservation's own time selectable even if it is off the grid
            // (older reservations, or the doctor's hours changed since booking).
            if (existing != null && model.DoctorId == existing.DoctorId && model.AppointmentDate == existing.AppointmentAt.Date)
            {
                var own = existing.AppointmentAt.ToString("HH:mm");
                if (slots.Slots.All(s => s.Time != own))
                {
                    slots.Slots.Add(new SlotViewModel { Time = own, Capacity = 1 });
                    slots.Slots.Sort((a, b) => string.CompareOrdinal(a.Time, b.Time));
                }
            }
            ViewBag.Slots = slots;

            ViewBag.LockedPrices = existing?.ReserveExaminationTypes?
                .ToDictionary(x => x.ExaminationTypeId, x => x.Price) ?? new Dictionary<int, decimal>();
            model.HasPayments = existing?.Payments?.Any() == true;
            model.CanReschedule = existing == null || ReserveFlow.CanReschedule(existing);
        }

        private List<ExaminationType> ValidateForm(ReserveFormViewModel model, Reserve? existing, out DateTime start, out SlotResult? slot)
        {
            start = existing?.AppointmentAt ?? default;
            slot = null;

            var selectedIds = (model.ExaminationTypeIds ?? new()).Distinct().ToList();
            var examTypes = _context.ExaminationTypes.Where(e => selectedIds.Contains(e.Id)).ToList();
            if (examTypes.Count == 0)
                ModelState.AddModelError(nameof(model.ExaminationTypeIds), "Ən azı bir müayinə növü seçin");

            if (model.Birthday.HasValue && ModelState.ContainsKey(nameof(model.Birthday)))
            {
                if (model.Birthday.Value.Date > ClinicTime.Today)
                    ModelState.AddModelError(nameof(model.Birthday), "Doğum tarixi gələcəkdə ola bilməz");
                else if (model.Birthday.Value.Year < 1900)
                    ModelState.AddModelError(nameof(model.Birthday), "Doğum tarixi düzgün deyil");
            }

            if (model.DoctorId != null && model.AppointmentDate.HasValue && !string.IsNullOrEmpty(model.AppointmentTime))
            {
                var requested = model.AppointmentDate.Value.Date
                    + (TimeSpan.TryParse(model.AppointmentTime, out var t) ? t : TimeSpan.Zero);

                // An unchanged appointment stays valid even if it is in the past or off the grid.
                var unchanged = existing != null
                    && model.DoctorId == existing.DoctorId
                    && requested == TruncateToMinute(existing.AppointmentAt);

                if (!unchanged)
                {
                    var error = _slots.Validate(model.DoctorId.Value, model.AppointmentDate.Value, model.AppointmentTime, existing?.Id, out start, out var s);
                    slot = s;
                    if (error != null)
                        ModelState.AddModelError(nameof(model.AppointmentTime), error);
                }
            }

            return examTypes;
        }

        private static DateTime TruncateToMinute(DateTime d) =>
            new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Kind);
    }
}
