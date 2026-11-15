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
    [HasPermission(Permissions.PaymentsTake, Permissions.KassaTotals)]
    public class KassaController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public KassaController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        // filter: unpaid | paid | refund | (empty = all)
        public IActionResult Index(string? filter, string? q)
        {
            var query = _context.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.ReserveExaminationTypes)
                .Include(r => r.Payments)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(r =>
                    EF.Functions.ILike(r.Patient.Name + " " + r.Patient.Surname, term) ||
                    EF.Functions.ILike(r.Patient.PinCode, term) ||
                    EF.Functions.ILike(r.Patient.Phone, term));
            }

            var items = query
                .OrderByDescending(r => r.AppointmentAt)
                .Take(500)
                .ToList()
                .Select(r => new KassaItemViewModel { Reserve = r, Total = r.AmountDue, Paid = r.Paid })
                .ToList();

            items = filter switch
            {
                "unpaid" => items.Where(i => i.PaymentStatus is PayStatus.Unpaid or PayStatus.Partial).ToList(),
                "paid" => items.Where(i => i.PaymentStatus == PayStatus.Paid).ToList(),
                "refund" => items.Where(i => i.PaymentStatus == PayStatus.Refund).ToList(),
                _ => items
            };

            var model = new KassaListViewModel
            {
                Items = items,
                Filter = filter,
                Query = q,
                TotalExpected = items.Sum(i => i.Total),
                TotalCollected = items.Sum(i => i.Paid),
                TotalOutstanding = items.Sum(i => i.Balance > 0 ? i.Balance : 0),
                TotalToRefund = items.Sum(i => i.Balance < 0 ? -i.Balance : 0)
            };
            return View(model);
        }

        public IActionResult Pay(int id)
        {
            var reserve = LoadReserve(id);
            if (reserve == null) return NotFound();

            var model = BuildModel(reserve);
            model.Amount = Math.Abs(model.Balance);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.PaymentsTake)]
        public IActionResult Pay(PaymentViewModel input)
        {
            var reserve = LoadReserve(input.ReserveId);
            if (reserve == null) return NotFound();

            var model = BuildModel(reserve);
            var amount = Math.Round(input.Amount, 2);

            if (StatusNames.IsClosedWithoutVisit(reserve.Status?.Name))
                ModelState.AddModelError("", $"«{StatusNames.Label(reserve.Status?.Name)}» rezervasiyaya ödəniş qəbul etmək olmaz");
            else if (amount <= 0)
                ModelState.AddModelError(nameof(input.Amount), "Məbləğ 0-dan böyük olmalıdır");
            else if (amount > model.Balance)
                ModelState.AddModelError(nameof(input.Amount),
                    model.Balance > 0
                        ? $"Məbləğ qalıq borcdan ({Fmt.Money(model.Balance)}) çox ola bilməz"
                        : "Bu rezervasiya artıq tam ödənilib");

            if (!Enum.IsDefined(input.Method))
                ModelState.AddModelError(nameof(input.Method), "Ödəniş üsulu düzgün deyil");

            if (ModelState.IsValid)
            {
                AddPayment(reserve.Id, amount, input);
                _audit.Log("Ödəniş qəbul edildi", AuditEntities.Payment, reserve.Id,
                    $"{reserve.Patient.FullName}: {Fmt.Money(amount)} ({(input.Method == PaymentMethod.Cash ? "nağd" : "kart")})");
                TempData["Success"] = $"{Fmt.Money(amount)} ödəniş qeydə alındı";
                return RedirectToAction(nameof(Pay), new { id = reserve.Id });
            }

            return Redisplay(model, input);
        }

        /// <summary>Gives money back when more was paid than is due (e.g. the reservation was cancelled).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.PaymentsRefund)]
        public IActionResult Refund(PaymentViewModel input)
        {
            var reserve = LoadReserve(input.ReserveId);
            if (reserve == null) return NotFound();

            var model = BuildModel(reserve);
            var amount = Math.Round(input.Amount, 2);
            var refundable = -model.Balance;

            if (refundable <= 0)
                ModelState.AddModelError("", "Bu rezervasiya üzrə qaytarılacaq məbləğ yoxdur");
            else if (amount <= 0)
                ModelState.AddModelError(nameof(input.Amount), "Məbləğ 0-dan böyük olmalıdır");
            else if (amount > refundable)
                ModelState.AddModelError(nameof(input.Amount), $"Ən çox {Fmt.Money(refundable)} qaytarmaq olar");

            if (!Enum.IsDefined(input.Method))
                ModelState.AddModelError(nameof(input.Method), "Ödəniş üsulu düzgün deyil");

            if (ModelState.IsValid)
            {
                AddPayment(reserve.Id, -amount, input);
                _audit.Log("Geri qaytarma", AuditEntities.Payment, reserve.Id,
                    $"{reserve.Patient.FullName}: {Fmt.Money(amount)} ({(input.Method == PaymentMethod.Cash ? "nağd" : "kart")})");
                TempData["Success"] = $"{Fmt.Money(amount)} xəstəyə qaytarıldı";
                return RedirectToAction(nameof(Pay), new { id = reserve.Id });
            }

            return Redisplay(model, input);
        }

        private void AddPayment(int reserveId, decimal amount, PaymentViewModel input)
        {
            _context.Payments.Add(new Payment
            {
                ReserveId = reserveId,
                Amount = amount,
                Method = input.Method,
                PaidAt = DateTime.Now,
                Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
            });
            _context.SaveChanges();
        }

        private IActionResult Redisplay(PaymentViewModel model, PaymentViewModel input)
        {
            model.Amount = input.Amount;
            model.Method = input.Method;
            model.Note = input.Note;
            return View(nameof(Pay), model);
        }

        private static PaymentViewModel BuildModel(Reserve reserve)
        {
            var total = reserve.AmountDue;
            var paid = reserve.Paid;
            return new PaymentViewModel
            {
                Reserve = reserve,
                ReserveId = reserve.Id,
                Total = total,
                Paid = paid,
                Balance = total - paid,
                Payments = reserve.Payments?.OrderByDescending(p => p.PaidAt).ToList() ?? new()
            };
        }

        private Reserve? LoadReserve(int id)
        {
            return _context.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(rt => rt.ExaminationType)
                .Include(r => r.Payments)
                .FirstOrDefault(r => r.Id == id);
        }
    }
}
