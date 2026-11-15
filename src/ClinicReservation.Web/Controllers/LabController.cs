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
    /// <summary>Laboratoriya: sample collection, result entry, validation and reports.</summary>
    [HasPermission(Permissions.LabView, Permissions.LabWork, Permissions.LabValidate)]
    public class LabController : Controller
    {
        private readonly AppDbContext _context;
        private readonly LabService _lab;
        private readonly SmsService _sms;
        private readonly AuditService _audit;

        public LabController(AppDbContext context, LabService lab, SmsService sms, AuditService audit)
        {
            _context = context;
            _lab = lab;
            _sms = sms;
            _audit = audit;
        }

        // tab: pending (sample not taken yet) | work (results being entered) | done (validated)
        public IActionResult Index(string? tab, DateTime? date, string? q)
        {
            var day = (date ?? ClinicTime.Today).Date;
            q = q?.Trim();

            // A barcode scanner types the 10 digits + Enter into the search box.
            if (!string.IsNullOrEmpty(q) && q.Length == 10 && q.All(char.IsDigit))
            {
                var hit = _context.LabOrders.Where(o => o.Barcode == q).Select(o => (int?)o.Id).FirstOrDefault();
                if (hit != null) return RedirectToAction(nameof(Order), new { id = hit });
                TempData["Error"] = $"{q} barkodlu nümunə tapılmadı";
            }

            var model = new LabWorklistViewModel { Tab = tab is "work" or "done" ? tab : "pending", Date = day, Query = q };

            var pending = PendingFor(day, q);
            var work = OrdersQuery(q).Where(o => o.Status == LabOrderStatus.Collected);
            var done = OrdersQuery(q).Where(o => o.Status == LabOrderStatus.Validated && o.ValidatedAt >= day && o.ValidatedAt < day.AddDays(1));

            model.PendingCount = pending.Count;
            model.WorkCount = work.Count();
            model.DoneCount = done.Count();
            model.Pending = pending;
            model.Orders = model.Tab switch
            {
                "work" => work.OrderBy(o => o.CollectedAt).ToList(),
                "done" => done.OrderByDescending(o => o.ValidatedAt).ToList(),
                _ => new List<LabOrder>()
            };
            return View(model);
        }

        /// <summary>Takes the sample: creates the order with a barcode and opens the label.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork)]
        public IActionResult Collect(int reserveId, string? returnTo)
        {
            var reserve = _context.Reserves.Include(r => r.Patient).Include(r => r.Status).FirstOrDefault(r => r.Id == reserveId);
            if (reserve == null) return NotFound();
            if (StatusNames.IsClosedWithoutVisit(reserve.Status?.Name))
            {
                TempData["Error"] = $"«{StatusNames.Label(reserve.Status?.Name)}» rezervasiyadan nümunə götürülmür";
                return RedirectToAction(nameof(Index));
            }

            var tests = _lab.PendingTests(reserveId);
            if (tests.Count == 0)
            {
                TempData["Error"] = "Bu rezervasiya üçün götürüləcək analiz yoxdur";
                return RedirectToAction(nameof(Index));
            }

            // One tube per sample type (blood and urine get separate barcodes).
            var orders = tests.GroupBy(t => t.SampleType)
                .Select(g => _lab.CreateOrder(reserve, g, reserve.Patient.Gender, User.Identity?.Name, ClinicTime.Now))
                .ToList();
            foreach (var o in orders)
                _audit.Log("Nümunə götürüldü", AuditEntities.Lab, o.Id, $"{reserve.Patient.FullName}, barkod {o.Barcode}, {o.Results.Count} analiz");

            TempData["Success"] = $"Nümunə götürüldü: {string.Join(", ", orders.Select(o => o.Barcode))}";
            // Tubes, needles, reagents of the lab services leave stock when the sample is taken.
            var warn = StockService.Warning(HttpContext.RequestServices.GetRequiredService<StockService>()
                .ConsumeForReserve(reserveId, true, User.Identity?.Name));
            if (warn != null) TempData["Error"] = warn;
            TempData["PrintLabels"] = string.Join(",", orders.Select(o => o.Id));
            return returnTo == "reservation"
                ? RedirectToAction("Details", "Reservation", new { id = reserveId })
                : RedirectToAction(nameof(Index), new { tab = "work" });
        }

        public IActionResult Order(int id)
        {
            var order = LoadOrder(id);
            if (order == null) return NotFound();
            _audit.Log("Analiz nəticəsinə baxıldı", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode}");
            return View(BuildModel(order));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork, Permissions.LabValidate)]
        public async Task<IActionResult> Save(LabSaveInput input)
        {
            var order = LoadOrder(input.OrderId);
            if (order == null) return NotFound();
            if (order.Status != LabOrderStatus.Collected)
            {
                TempData["Error"] = "Təsdiqlənmiş və ya ləğv edilmiş nümunəyə dəyişiklik edilmir";
                return RedirectToAction(nameof(Order), new { id = order.Id });
            }

            var validate = input.Validate == "1";
            if (validate && !User.Can(Permissions.LabValidate))
                return Forbid();

            var now = ClinicTime.Now;
            var userName = User.Identity?.Name;
            var gender = order.Patient.Gender;
            var errors = new List<string>();
            var changed = 0;

            foreach (var r in order.Results)
            {
                var posted = input.Results.FirstOrDefault(x => x.Id == r.Id);
                var value = string.IsNullOrWhiteSpace(posted?.Value) ? null : posted!.Value!.Trim();
                var t = r.LabTest;

                if (value != null && t.ResultType == LabResultType.Numeric && !LabRef.TryParseNumber(value, out _))
                    errors.Add($"{t.Name}: rəqəm daxil edin");
                else if (value != null && t.ResultType == LabResultType.Choice && t.ChoiceList.Length > 0
                         && !t.ChoiceList.Contains(value, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{t.Name}: siyahıdan seçin");

                if (value != r.Value)
                {
                    r.Value = value;
                    r.EnteredAt = now;
                    r.EnteredBy = userName;
                    changed++;
                }
                var (flag, number) = LabRef.Evaluate(t, gender, value);
                r.Flag = flag;
                r.NumericValue = number;
                r.Unit = t.Unit;
                r.Reference = LabRef.Text(t, gender);
            }
            order.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

            if (validate)
            {
                if (order.Results.Any(r => r.Value == null))
                    errors.Add("Təsdiq üçün bütün nəticələr yazılmalıdır");
                if (order.HasCritical && !input.ConfirmCritical)
                    errors.Add("Kritik dəyər var — yoxlayın və «Kritik dəyəri yoxladım» işarəsini qoyun");
            }

            if (errors.Count > 0)
            {
                // Show what was typed, nothing is saved.
                ViewBag.Errors = errors;
                ViewBag.SendSms = input.SendSms;
                return View("Order", BuildModel(order));
            }

            if (validate)
            {
                order.Status = LabOrderStatus.Validated;
                order.ValidatedAt = now;
                order.ValidatedBy = userName;
            }
            _context.SaveChanges();

            if (changed > 0)
                _audit.Log("Analiz nəticəsi yazıldı", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode}: {changed} nəticə");

            if (!validate)
            {
                TempData["Success"] = "Nəticələr saxlanıldı";
                return RedirectToAction(nameof(Order), new { id = order.Id });
            }

            _audit.Log("Analiz nəticəsi təsdiqləndi", AuditEntities.Lab, order.Id,
                $"{order.Patient.FullName}, {order.Barcode}" + (order.HasCritical ? " — KRİTİK DƏYƏR" : order.HasAbnormal ? " — normadan kənar" : ""));

            var message = "Nəticələr təsdiqləndi";
            if (input.SendSms)
            {
                var reserve = order.Reserve;
                reserve.Patient = order.Patient;
                var allDone = !_context.LabOrders.Any(o => o.ReserveId == reserve.Id && o.Status == LabOrderStatus.Collected)
                              && _lab.PendingTests(reserve.Id).Count == 0;
                if (allDone)
                {
                    var sms = await _sms.SendAsync(reserve, _sms.LabResultText(reserve), SmsService.KindLabResult, userName);
                    message += sms.Status switch
                    {
                        SmsStatus.Failed => $". SMS göndərilmədi: {sms.Error}",
                        SmsStatus.Simulated => ". Xəstəyə SMS (test rejimində) qeydə alındı",
                        _ => ". Xəstəyə SMS göndərildi"
                    };
                }
                else
                {
                    message += ". SMS bu xəstənin bütün nümunələri təsdiqlənəndə göndəriləcək";
                }
            }
            TempData["Success"] = message;
            return RedirectToAction(nameof(Order), new { id = order.Id });
        }

        /// <summary>Re-opens a validated order for correction (logged).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabValidate)]
        public IActionResult Reopen(int id, string? reason)
        {
            var order = _context.LabOrders.Include(o => o.Patient).FirstOrDefault(o => o.Id == id);
            if (order == null) return NotFound();
            if (order.Status != LabOrderStatus.Validated) return RedirectToAction(nameof(Order), new { id });
            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Error"] = "Düzəliş üçün səbəb yazın";
                return RedirectToAction(nameof(Order), new { id });
            }

            order.Status = LabOrderStatus.Collected;
            order.ValidatedAt = null;
            order.ValidatedBy = null;
            _context.SaveChanges();
            _audit.Log("Analiz təsdiqi geri alındı", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode} — səbəb: {reason.Trim()}");
            TempData["Success"] = "Nəticə düzəliş üçün açıldı. Düzəlişdən sonra yenidən təsdiqləyin.";
            return RedirectToAction(nameof(Order), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork)]
        public IActionResult Cancel(int id, string? reason)
        {
            var order = _context.LabOrders.Include(o => o.Patient).FirstOrDefault(o => o.Id == id);
            if (order == null) return NotFound();
            if (order.Status != LabOrderStatus.Collected)
            {
                TempData["Error"] = "Yalnız təsdiqlənməmiş nümunə ləğv edilə bilər";
                return RedirectToAction(nameof(Order), new { id });
            }
            order.Status = LabOrderStatus.Cancelled;
            order.CancelledAt = ClinicTime.Now;
            order.Note = string.IsNullOrWhiteSpace(reason) ? order.Note : $"Ləğv: {reason.Trim()}";
            _context.SaveChanges();
            _audit.Log("Nümunə ləğv edildi", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode}" + (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason.Trim()}"));
            TempData["Success"] = "Nümunə ləğv edildi. Analizlər yenidən «Nümunə gözləyir» siyahısına düşdü.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Records the patient's sex (needed for some reference ranges).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.LabWork)]
        public IActionResult SetGender(int id, Gender gender)
        {
            var order = _context.LabOrders.Include(o => o.Patient).FirstOrDefault(o => o.Id == id);
            if (order == null) return NotFound();
            order.Patient.Gender = gender;
            _context.SaveChanges();
            _audit.Log("Xəstənin cinsi qeyd edildi", AuditEntities.Patient, order.PatientId, $"{order.Patient.FullName}: {Fmt.Gender(gender)}");
            return RedirectToAction(nameof(Order), new { id });
        }

        /// <summary>Barcode sticker(s) for the tube(s). ids = comma separated order ids.</summary>
        public IActionResult Label(string ids)
        {
            var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToList();
            var orders = _context.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .Where(o => list.Contains(o.Id))
                .ToList();
            if (orders.Count == 0) return NotFound();
            return View(orders);
        }

        /// <summary>A4 result report (print / save as PDF).</summary>
        public IActionResult Report(int id)
        {
            var order = LoadOrder(id);
            if (order == null) return NotFound();
            if (order.Status != LabOrderStatus.Validated)
            {
                TempData["Error"] = "Nəticə blankı yalnız təsdiqdən sonra çap olunur";
                return RedirectToAction(nameof(Order), new { id });
            }
            _audit.Log("Analiz nəticəsi çap edildi", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode}");
            return View(BuildModel(order));
        }

        // ---------- helpers ----------

        private LabOrderViewModel BuildModel(LabOrder order) => new()
        {
            Order = order,
            Previous = _lab.PreviousResults(order.PatientId, order.Id, order.Results.Select(r => r.LabTestId)),
            CanEdit = order.Status == LabOrderStatus.Collected && User.CanAny(Permissions.LabWork, Permissions.LabValidate)
        };

        private LabOrder? LoadOrder(int id) =>
            _context.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Reserve).ThenInclude(r => r.Doctor)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .AsSplitQuery()
                .FirstOrDefault(o => o.Id == id);

        private IQueryable<LabOrder> OrdersQuery(string? q)
        {
            var query = _context.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Reserve).ThenInclude(r => r.Doctor)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .AsSplitQuery();
            if (!string.IsNullOrEmpty(q))
            {
                var term = $"%{q}%";
                query = query.Where(o => EF.Functions.ILike(o.Patient.Name + " " + o.Patient.Surname, term)
                                         || EF.Functions.ILike(o.Patient.PinCode, term) || o.Barcode.Contains(q));
            }
            return query;
        }

        /// <summary>Reservations on the day whose lab tests have no sample yet.</summary>
        private List<LabPendingRow> PendingFor(DateTime day, string? q)
        {
            var query = _context.Reserves
                .Include(r => r.Patient)
                .Include(r => r.Status)
                .Include(r => r.Doctor)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType).ThenInclude(e => e.LabTests).ThenInclude(l => l.LabTest)
                .Where(r => r.AppointmentAt >= day && r.AppointmentAt < day.AddDays(1))
                .Where(r => r.Status!.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow)
                .Where(r => r.ReserveExaminationTypes!.Any(x => x.ExaminationType.LabTests.Any()));
            if (!string.IsNullOrEmpty(q))
            {
                var term = $"%{q}%";
                query = query.Where(r => EF.Functions.ILike(r.Patient.Name + " " + r.Patient.Surname, term) || EF.Functions.ILike(r.Patient.PinCode, term));
            }
            var reserves = query.OrderBy(r => r.AppointmentAt).AsSplitQuery().ToList();
            if (reserves.Count == 0) return new();

            var ids = reserves.Select(r => r.Id).ToList();
            var taken = _context.LabResults
                .Where(r => ids.Contains(r.LabOrder.ReserveId) && r.LabOrder.Status != LabOrderStatus.Cancelled)
                .Select(r => new { r.LabOrder.ReserveId, r.LabTestId })
                .ToList()
                .ToLookup(x => x.ReserveId, x => x.LabTestId);

            return reserves
                .Select(r => new LabPendingRow
                {
                    Reserve = r,
                    Tests = r.ReserveExaminationTypes!
                        .SelectMany(x => x.ExaminationType.LabTests.Select(l => l.LabTest))
                        .Where(t => t.IsActive && !taken[r.Id].Contains(t.Id))
                        .DistinctBy(t => t.Id)
                        .OrderBy(t => t.SortOrder)
                        .ToList()
                })
                .Where(x => x.Tests.Count > 0)
                .ToList();
        }
    }
}
