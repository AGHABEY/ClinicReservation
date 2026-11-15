using System.Globalization;
using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Stasionar: bed board, admission, daily notes, medication orders and round, discharge.</summary>
    [HasPermission(Permissions.InpatientView, Permissions.InpatientManage, Permissions.InpatientDoctor, Permissions.InpatientNurse)]
    public class InpatientController : Controller
    {
        private readonly AppDbContext _context;
        private readonly InpatientService _inpatient;
        private readonly StockService _stock;
        private readonly AuditService _audit;

        public InpatientController(AppDbContext context, InpatientService inpatient, StockService stock, AuditService audit)
        {
            _context = context;
            _inpatient = inpatient;
            _stock = stock;
            _audit = audit;
        }

        private string? UserName => User.Identity?.Name;

        // ---------- Board and lists ----------

        public IActionResult Index(int? departmentId)
        {
            var today = DateTime.Today;
            return View(new InpatientBoardViewModel
            {
                Beds = _inpatient.Board(departmentId),
                Departments = _context.Wards.Where(w => w.IsActive && w.Department != null).Select(w => w.Department!).Distinct().OrderBy(d => d.Name).ToList(),
                DepartmentId = departmentId,
                AdmittedToday = _context.Admissions.Count(a => a.Status != AdmissionStatus.Cancelled && a.AdmittedAt >= today),
                DischargedToday = _context.Admissions.Count(a => a.Status == AdmissionStatus.Discharged && a.DischargedAt >= today),
                ActiveTotal = _context.Admissions.Count(a => a.Status == AdmissionStatus.Active)
            });
        }

        public IActionResult List(string? status, DateTime? from, DateTime? to, string? q)
        {
            var end = (to ?? DateTime.Today).Date;
            var start = (from ?? end.AddDays(-30)).Date;
            if (start > end) (start, end) = (end, start);
            q = q?.Trim();
            var model = new InpatientListViewModel { Status = status == "discharged" ? "discharged" : "active", From = start, To = end, Query = q };

            var query = _context.Admissions.Include(a => a.Patient).Include(a => a.Doctor).Include(a => a.Department)
                .Include(a => a.Bed).ThenInclude(b => b!.Ward).AsQueryable();
            query = model.Status == "active"
                ? query.Where(a => a.Status == AdmissionStatus.Active)
                : query.Where(a => a.Status != AdmissionStatus.Active && a.DischargedAt >= start && a.DischargedAt < end.AddDays(1));
            if (!string.IsNullOrEmpty(q))
            {
                var like = $"%{q}%";
                query = query.Where(a => EF.Functions.ILike(a.Patient.Name + " " + a.Patient.Surname, like)
                    || EF.Functions.ILike(a.Patient.PinCode, like) || EF.Functions.ILike(a.Number, like));
            }
            model.Admissions = (model.Status == "active" ? query.OrderBy(a => a.AdmittedAt) : query.OrderByDescending(a => a.DischargedAt))
                .Take(300).ToList();
            return View(model);
        }

        // ---------- Admission ----------

        [HasPermission(Permissions.InpatientManage, Permissions.InpatientDoctor)]
        public IActionResult Admit(int? patientId, int? bedId, int? reserveId)
        {
            var model = new AdmitViewModel { PatientId = patientId, BedId = bedId, ReserveId = reserveId };
            if (reserveId != null)
            {
                var r = _context.Reserves.Include(x => x.MedicalRecord).FirstOrDefault(x => x.Id == reserveId);
                if (r == null) return NotFound();
                model.PatientId = r.PatientId;
                model.DoctorId = r.DoctorId;
                model.IcdCode = r.MedicalRecord?.IcdCode;
                model.Diagnosis = r.MedicalRecord?.Diagnosis;
            }
            return View(PrepareAdmit(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientManage, Permissions.InpatientDoctor)]
        public IActionResult Admit(AdmitViewModel model)
        {
            if (model.PatientId == null && !string.IsNullOrWhiteSpace(model.PatientPin))
            {
                var pin = Patient.NormalizePin(model.PatientPin);
                model.PatientId = _context.Patients.Where(p => p.PinCode == pin).Select(p => (int?)p.Id).FirstOrDefault();
                if (model.PatientId == null) ModelState.AddModelError(nameof(model.PatientPin), $"{pin} FİN-li xəstə tapılmadı — əvvəlcə xəstə kartını yaradın");
            }
            else if (model.PatientId == null)
                ModelState.AddModelError(nameof(model.PatientPin), "Xəstənin FİN-ini yazın");
            if (!ModelState.IsValid) return View(PrepareAdmit(model));

            var r = _inpatient.Admit(model.PatientId!.Value, model.BedId!.Value, model.DoctorId, model.Type,
                Clean(model.IcdCode)?.ToUpperInvariant(), Clean(model.Diagnosis), Clean(model.Note), model.ReserveId, UserName);
            if (!r.Ok)
            {
                ModelState.AddModelError("", r.Error!);
                return View(PrepareAdmit(model));
            }
            var a = Load(r.Admission!.Id)!;
            _audit.Log("Stasionara yerləşdirildi", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}: {a.Bed!.Label}, {InpatientLabels.Type(a.Type)}, {a.AdmitIcdCode} {a.AdmitDiagnosis}".Trim());
            var warn = InpatientService.GenderWarning(a.Bed, a.Patient);
            TempData["Success"] = $"{a.Patient.FullName} yerləşdirildi: {a.Bed.Label} ({a.Number})";
            if (warn != null) TempData["Error"] = $"Diqqət: {warn}";
            return RedirectToAction(nameof(Details), new { id = a.Id });
        }

        private AdmitViewModel PrepareAdmit(AdmitViewModel m)
        {
            if (m.PatientId != null) m.Patient = _context.Patients.Find(m.PatientId);
            m.FreeBeds = _inpatient.FreeBeds();
            m.Doctors = _context.Doctors.Where(d => d.IsActive || d.Id == m.DoctorId).OrderBy(d => d.FullName).ToList();
            return m;
        }

        // ---------- Case page ----------

        public IActionResult Details(int id, string? tab, DateTime? day)
        {
            var a = Load(id);
            if (a == null) return NotFound();
            return View(Build(a, tab, day));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientManage)]
        public IActionResult Move(int id, int bedId)
        {
            var a = Load(id);
            if (a == null) return NotFound();
            var from = a.Bed?.Label;
            var r = _inpatient.Move(a, bedId, UserName);
            if (!r.Ok) TempData["Error"] = r.Error;
            else
            {
                a = Load(id)!;
                _audit.Log("Çarpayı dəyişdi", AuditEntities.Inpatient, a.Id, $"{a.Number} {a.Patient.FullName}: {from} → {a.Bed!.Label}");
                TempData["Success"] = $"{a.Patient.FullName} köçürüldü: {a.Bed.Label}";
                var warn = InpatientService.GenderWarning(a.Bed, a.Patient);
                if (warn != null) TempData["Error"] = $"Diqqət: {warn}";
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor, Permissions.InpatientNurse)]
        public IActionResult Observe(ObservationInput input)
        {
            var a = Load(input.AdmissionId);
            if (a == null) return NotFound();
            if (input.Kind == ObservationKind.Doctor && !User.Can(Permissions.InpatientDoctor)) return Forbid();
            if (!Enum.IsDefined(input.Kind)) return BadRequest();

            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (!a.IsActive) errors.Add("Xəstə çıxarılıb — yeni qeyd yazılmır");
            var text = Clean(input.Text);
            var hasVitals = input.Temperature != null || input.Pulse != null || input.BpSystolic != null || input.BpDiastolic != null
                || input.RespRate != null || input.SpO2 != null;
            if (input.Kind == ObservationKind.Doctor && text == null) errors.Add("Gündəlik qeydini yazın");
            if (input.Kind == ObservationKind.Nurse && text == null && !hasVitals) errors.Add("Vital göstərici və ya qeyd yazın");
            if ((input.BpSystolic == null) != (input.BpDiastolic == null)) errors.Add("Təzyiqin hər iki rəqəmini yazın (məs. 120 / 80)");
            else if (input.BpSystolic <= input.BpDiastolic) errors.Add("Sistolik təzyiq diastolikdən böyük olmalıdır");
            if (errors.Any())
            {
                var vm = Build(a, "journal", null);
                vm.NewObservation = input;
                vm.Errors = errors.Distinct().ToList();
                return View(nameof(Details), vm);
            }

            var o = new Observation
            {
                AdmissionId = a.Id, Kind = input.Kind, At = DateTime.Now, Author = UserName, Text = text,
                Temperature = input.Temperature, Pulse = input.Pulse, BpSystolic = input.BpSystolic, BpDiastolic = input.BpDiastolic,
                RespRate = input.RespRate, SpO2 = input.SpO2
            };
            _context.Observations.Add(o);
            _context.SaveChanges();
            _audit.Log(input.Kind == ObservationKind.Doctor ? "Həkim gündəliyi yazıldı" : "Tibb bacısı qeydi yazıldı", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}" + (hasVitals ? $"; {Vitals(o)}" : ""));
            TempData["Success"] = "Qeyd saxlanıldı";
            return RedirectToAction(nameof(Details), new { id = a.Id, tab = "journal" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor)]
        public IActionResult Order(OrderInput input)
        {
            var a = Load(input.AdmissionId);
            if (a == null) return NotFound();
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (!a.IsActive) errors.Add("Xəstə çıxarılıb — yeni təyinat yazılmır");

            StockItem? item = null;
            if (input.StockItemId != null)
            {
                item = _context.StockItems.FirstOrDefault(i => i.Id == input.StockItemId && i.IsActive);
                if (item == null) errors.Add("Dərman tapılmadı");
            }
            var name = Clean(input.DrugName) ?? item?.DisplayName;
            if (name == null) errors.Add("Dərmanı seçin və ya adını yazın");
            var route = Clean(input.Route);
            if (route != null && !InpatientLabels.Routes.Contains(route)) errors.Add("Yeridilmə yolunu siyahıdan seçin");
            var times = ParseTimes(input.Times, out var timesError);
            if (timesError != null) errors.Add(timesError);

            if (errors.Any())
            {
                var vm = Build(a, "orders", null);
                vm.NewOrder = input;
                vm.Errors = errors.Distinct().ToList();
                return View(nameof(Details), vm);
            }

            var now = DateTime.Now;
            var order = new MedicationOrder
            {
                AdmissionId = a.Id,
                StockItemId = item?.Id,
                DrugName = name!,
                Dose = Clean(input.Dose),
                QuantityPerDose = item != null ? input.QuantityPerDose : 1,
                Route = route,
                Times = times,
                StartAt = now,
                EndDate = input.Days != null ? now.Date.AddDays(input.Days.Value - 1) : null,
                Note = Clean(input.Note),
                OrderedAt = now,
                OrderedBy = UserName
            };
            _context.MedicationOrders.Add(order);
            _context.SaveChanges();
            _audit.Log("Dərman təyin edildi", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}: {order.Name} {order.Dose} {order.Route} — {(order.IsPrn ? "lazım olduqda" : order.Times)}"
                + (order.EndDate != null ? $", {input.Days} gün" : ""));
            TempData["Success"] = $"{order.Name} təyin edildi";
            return RedirectToAction(nameof(Details), new { id = a.Id, tab = "orders" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor)]
        public IActionResult StopOrder(int id, int orderId)
        {
            var o = _context.MedicationOrders.Include(x => x.Admission).ThenInclude(x => x.Patient).FirstOrDefault(x => x.Id == orderId && x.AdmissionId == id);
            if (o == null) return NotFound();
            if (o.StoppedAt != null)
                TempData["Error"] = "Təyinat artıq dayandırılıb";
            else
            {
                o.StoppedAt = DateTime.Now;
                o.StoppedBy = UserName;
                _context.SaveChanges();
                _audit.Log("Təyinat dayandırıldı", AuditEntities.Inpatient, id, $"{o.Admission.Number} {o.Admission.Patient.FullName}: {o.Name}");
                TempData["Success"] = $"{o.Name} dayandırıldı";
            }
            return RedirectToAction(nameof(Details), new { id, tab = "orders" });
        }

        /// <summary>Medication round: a nurse marks a scheduled dose as given or not given.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientNurse)]
        public IActionResult Dose(int id, int orderId, string? slot, DoseStatus status, string? note, DateTime? day)
        {
            var o = _context.MedicationOrders.Include(x => x.StockItem).Include(x => x.Admission).ThenInclude(x => x.Patient)
                .FirstOrDefault(x => x.Id == orderId && x.AdmissionId == id);
            if (o == null) return NotFound();
            if (!Enum.IsDefined(status)) return BadRequest();
            DateTime at = default;
            if (!o.IsPrn && !DateTime.TryParseExact(slot, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
                return BadRequest();

            var r = _inpatient.RecordDose(o, at, status, note, UserName, out var stockWarning);
            if (!r.Ok) TempData["Error"] = r.Error;
            else
            {
                _audit.Log(status == DoseStatus.Given ? "Dərman verildi" : "Dərman verilmədi", AuditEntities.Inpatient, id,
                    $"{o.Admission.Number} {o.Admission.Patient.FullName}: {o.Name} {o.Dose}, {(o.IsPrn ? "lazım olduqda" : Fmt.DateTime(at))}"
                    + (status == DoseStatus.Skipped ? $" — {note}" : ""));
                TempData["Success"] = status == DoseStatus.Given ? $"{o.Name} — verildi" : $"{o.Name} — verilmədi";
                if (stockWarning != null) TempData["Error"] = stockWarning;
            }
            return RedirectToAction(nameof(Details), new { id, tab = "mar", day = (day ?? DateTime.Today).ToString("yyyy-MM-dd") });
        }

        // ---------- Discharge ----------

        [HasPermission(Permissions.InpatientDoctor)]
        public IActionResult Discharge(int id)
        {
            var a = Load(id);
            if (a == null) return NotFound();
            if (!a.IsActive) return RedirectToAction(nameof(Epicrisis), new { id });
            // Pre-fill what the system already knows; the doctor edits it.
            var meds = a.Orders.OrderBy(o => o.StartAt)
                .Select(o => $"{o.Name}{(o.Dose != null ? " " + o.Dose : "")}{(o.Route != null ? ", " + o.Route.ToLower(Fmt.Az) : "")}"
                    + $" — {(o.IsPrn ? "lazım olduqda" : $"gündə {o.TimeList.Length} dəfə")}, {Fmt.Date(o.StartAt)}–{Fmt.Date(o.StoppedAt ?? DateTime.Today)}")
                .ToList();
            ViewBag.Admission = a;
            return View(new DischargeInput
            {
                AdmissionId = a.Id,
                IcdCode = a.AdmitIcdCode,
                Diagnosis = a.AdmitDiagnosis,
                Treatment = meds.Any() ? string.Join("\n", meds) : null
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientDoctor)]
        public IActionResult Discharge(DischargeInput input)
        {
            var a = Load(input.AdmissionId);
            if (a == null) return NotFound();
            if (!ModelState.IsValid)
            {
                ViewBag.Admission = a;
                return View(input);
            }
            var r = _inpatient.Discharge(a, input.Outcome!.Value, Clean(input.IcdCode)?.ToUpperInvariant(), Clean(input.Diagnosis)!,
                Clean(input.Course), Clean(input.Treatment), Clean(input.Recommendations), UserName);
            if (!r.Ok)
            {
                TempData["Error"] = r.Error;
                return RedirectToAction(nameof(Details), new { id = a.Id });
            }
            var (_, days, _) = InpatientService.BedDays(a, DateTime.Now);
            _audit.Log("Stasionardan çıxarıldı", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}: {InpatientLabels.Outcome(a.Outcome)}, {a.FinalIcdCode} {a.FinalDiagnosis}, {days} yataq-gün");
            TempData["Success"] = $"{a.Patient.FullName} çıxarıldı. Epikrizi çap edib xəstəyə verin.";
            return RedirectToAction(nameof(Epicrisis), new { id = a.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.InpatientManage)]
        public IActionResult Cancel(int id, string? reason)
        {
            var a = Load(id);
            if (a == null) return NotFound();
            var r = _inpatient.Cancel(a, reason ?? "", UserName);
            if (!r.Ok)
            {
                TempData["Error"] = r.Error;
                return RedirectToAction(nameof(Details), new { id });
            }
            _audit.Log("Yerləşdirmə ləğv edildi", AuditEntities.Inpatient, a.Id, $"{a.Number} {a.Patient.FullName}: {a.CancelReason}");
            TempData["Success"] = $"{a.Number} ləğv edildi, çarpayı boşaldı";
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Epicrisis(int id)
        {
            var a = Load(id);
            if (a == null) return NotFound();
            _audit.Log("Epikrizə baxıldı", AuditEntities.Inpatient, a.Id, $"{a.Number} {a.Patient.FullName}");
            return View(a);
        }

        // ------------------------------------------------------------------

        private AdmissionViewModel Build(Admission a, string? tab, DateTime? day)
        {
            var vm = new AdmissionViewModel
            {
                Admission = a,
                Tab = tab is "orders" or "mar" or "bill" ? tab : "journal",
                Day = (day ?? DateTime.Today).Date,
                NewObservation = new ObservationInput
                {
                    AdmissionId = a.Id,
                    Kind = User.Can(Permissions.InpatientDoctor) ? ObservationKind.Doctor : ObservationKind.Nurse
                },
                NewOrder = new OrderInput { AdmissionId = a.Id, Times = "08:00;20:00" },
                PreviousStays = _context.Admissions.Include(x => x.Department)
                    .Where(x => x.PatientId == a.PatientId && x.Id != a.Id && x.Status == AdmissionStatus.Discharged)
                    .OrderByDescending(x => x.AdmittedAt).Take(10).ToList()
            };
            if (a.IsActive && User.Can(Permissions.InpatientManage)) vm.FreeBeds = _inpatient.FreeBeds();
            if (a.IsActive && User.Can(Permissions.InpatientDoctor))
            {
                vm.Medicines = _context.StockItems.Where(i => i.IsActive && i.Kind == StockItemKind.Medicine)
                    .OrderBy(i => i.Name).ThenBy(i => i.Form).ToList();
                var wh = _context.Warehouses.Where(w => w.IsActive && w.DepartmentId != null && w.DepartmentId == a.DepartmentId)
                    .Select(w => (int?)w.Id).FirstOrDefault() ?? _stock.MainWarehouse()?.Id;
                if (wh != null) vm.OnHand = _stock.OnHand(wh, usableOnly: true);
            }
            return vm;
        }

        private Admission? Load(int id) => _context.Admissions
            .Include(a => a.Patient)
            .Include(a => a.Bed).ThenInclude(b => b!.Ward)
            .Include(a => a.Department)
            .Include(a => a.Doctor)
            .Include(a => a.Stays).ThenInclude(s => s.Bed).ThenInclude(b => b.Ward)
            .Include(a => a.Observations)
            .Include(a => a.Orders).ThenInclude(o => o.StockItem)
            .Include(a => a.Orders).ThenInclude(o => o.Doses)
            .AsSplitQuery()
            .FirstOrDefault(a => a.Id == id);

        /// <summary>"8:00; 20.00 ,14:00" → "08:00;14:00;20:00". Empty = as needed.</summary>
        private static string? ParseTimes(string? raw, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var list = new SortedSet<TimeSpan>();
            foreach (var part in raw.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TimeSpan.TryParseExact(part.Replace('.', ':'), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t)
                    || t >= TimeSpan.FromDays(1))
                {
                    error = $"«{part}» düzgün vaxt deyil (məs. 08:00)";
                    return null;
                }
                list.Add(t);
            }
            if (list.Count > 8) { error = "Gündə ən çox 8 qəbul vaxtı"; return null; }
            return string.Join(";", list.Select(t => t.ToString(@"hh\:mm")));
        }

        public static string Vitals(Observation o)
        {
            var parts = new List<string>();
            if (o.Temperature != null) parts.Add($"T {o.Temperature.Value.ToString("0.0", Fmt.Az)}°");
            if (o.BpSystolic != null) parts.Add($"AT {o.BpSystolic}/{o.BpDiastolic}");
            if (o.Pulse != null) parts.Add($"Nəbz {o.Pulse}");
            if (o.RespRate != null) parts.Add($"TS {o.RespRate}");
            if (o.SpO2 != null) parts.Add($"SpO₂ {o.SpO2}%");
            return string.Join(", ", parts);
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
