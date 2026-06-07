using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [Authorize]
    public class KassaController : Controller
    {
        private readonly AppDbContext _context;

        public KassaController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index(string? filter)
        {
            var reserves = _context.Reserves
                .Include(r => r.Status)
                .Include(r => r.ReserveExaminationTypes)!.ThenInclude(rt => rt.ExaminationType)
                .Include(r => r.Payments)
                .OrderByDescending(r => r.Id)
                .ToList();

            var items = reserves.Select(r => new KassaItemViewModel
            {
                Reserve = r,
                Total = r.ReserveExaminationTypes?.Sum(x => x.ExaminationType.Price) ?? 0,
                Paid = r.Payments?.Sum(p => p.Amount) ?? 0
            }).ToList();

            if (filter == "unpaid")
                items = items.Where(i => i.PaymentStatus != "Paid").ToList();
            else if (filter == "paid")
                items = items.Where(i => i.PaymentStatus == "Paid").ToList();

            var model = new KassaListViewModel
            {
                Items = items,
                Filter = filter,
                TotalExpected = items.Sum(i => i.Total),
                TotalCollected = items.Sum(i => i.Paid),
                TotalOutstanding = items.Sum(i => i.Balance > 0 ? i.Balance : 0)
            };
            return View(model);
        }

        public IActionResult Pay(int id)
        {
            var reserve = LoadReserve(id);
            if (reserve == null) return NotFound();

            var total = reserve.ReserveExaminationTypes?.Sum(x => x.ExaminationType.Price) ?? 0;
            var paid = reserve.Payments?.Sum(p => p.Amount) ?? 0;

            var model = new PaymentViewModel
            {
                Reserve = reserve,
                ReserveId = reserve.Id,
                Total = total,
                Paid = paid,
                Balance = total - paid,
                Amount = total - paid > 0 ? total - paid : 0,
                Payments = reserve.Payments?.OrderByDescending(p => p.Id).ToList() ?? new()
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Pay(PaymentViewModel model)
        {
            var reserve = LoadReserve(model.ReserveId);
            if (reserve == null) return NotFound();

            if (model.Amount <= 0)
                ModelState.AddModelError(nameof(model.Amount), "Məbləğ 0-dan böyük olmalıdır");

            if (ModelState.IsValid)
            {
                _context.Payments.Add(new Payment
                {
                    ReserveId = model.ReserveId,
                    Amount = model.Amount,
                    Method = model.Method,
                    PaidAt = DateTime.Now,
                    Note = model.Note
                });
                _context.SaveChanges();
                TempData["Success"] = $"{model.Amount} ₼ ödəniş qeydə alındı";
                return RedirectToAction("Pay", new { id = model.ReserveId });
            }

            // Re-populate display data on validation error
            var total = reserve.ReserveExaminationTypes?.Sum(x => x.ExaminationType.Price) ?? 0;
            var paid = reserve.Payments?.Sum(p => p.Amount) ?? 0;
            model.Reserve = reserve;
            model.Total = total;
            model.Paid = paid;
            model.Balance = total - paid;
            model.Payments = reserve.Payments?.OrderByDescending(p => p.Id).ToList() ?? new();
            return View(model);
        }

        private Reserve? LoadReserve(int id)
        {
            return _context.Reserves
                .Include(r => r.Status)
                .Include(r => r.ReserveExaminationTypes)!.ThenInclude(rt => rt.ExaminationType)
                .Include(r => r.Payments)
                .FirstOrDefault(r => r.Id == id);
        }
    }
}
