using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class ReportController : Controller
    {
        private readonly AppDbContext _context;

        public ReportController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index(DateTime? from, DateTime? to)
        {
            var fromDate = (from ?? DateTime.Today.AddDays(-29)).Date;
            var toDate = (to ?? DateTime.Today).Date;
            var toExclusive = toDate.AddDays(1);

            // Payments (actual cash flow) in the period
            var payments = _context.Payments
                .Include(p => p.Reserve)
                .Where(p => p.PaidAt >= fromDate && p.PaidAt < toExclusive)
                .OrderByDescending(p => p.PaidAt)
                .ToList();

            // Expected revenue by examination type, based on reservations created in the period
            var examStats = _context.ReserveExaminationTypes
                .Include(rt => rt.ExaminationType)
                .Include(rt => rt.Reserve)
                .Where(rt => rt.Reserve.AddedDate >= fromDate && rt.Reserve.AddedDate < toExclusive)
                .GroupBy(rt => rt.ExaminationType.Name)
                .Select(g => new ExamTypeStat
                {
                    Name = g.Key,
                    Count = g.Count(),
                    Expected = g.Sum(x => x.ExaminationType.Price)
                })
                .OrderByDescending(e => e.Expected)
                .ToList();

            var model = new ReportViewModel
            {
                From = fromDate,
                To = toDate,
                Payments = payments,
                PaymentCount = payments.Count,
                TotalIncome = payments.Sum(p => p.Amount),
                CashTotal = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
                CardTotal = payments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount),
                ExamTypeStats = examStats,
                DailyTotals = payments
                    .GroupBy(p => p.PaidAt.Date)
                    .Select(g => new DailyTotal { Date = g.Key, Count = g.Count(), Total = g.Sum(x => x.Amount) })
                    .OrderBy(d => d.Date)
                    .ToList()
            };

            return View(model);
        }
    }
}
