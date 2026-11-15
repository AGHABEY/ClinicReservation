using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Billing
{
    /// <summary>Hesabatlar: cash flow, revenue by examination and per-doctor workload for a period.</summary>
    public class ReportQueries
    {
        private readonly IAppDbContext _db;
        public ReportQueries(IAppDbContext db) => _db = db;

        public async Task<ReportViewModel> PeriodAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var fromDate = (from ?? ClinicTime.Today.AddDays(-29)).Date;
            var toDate = (to ?? ClinicTime.Today).Date;
            if (fromDate > toDate)
                (fromDate, toDate) = (toDate, fromDate);
            var toExclusive = toDate.AddDays(1);

            // Payments (actual cash flow) in the period
            var payments = await _db.Payments
                .Include(p => p.Reserve).ThenInclude(r => r!.Patient)
                .Where(p => p.PaidAt >= fromDate && p.PaidAt < toExclusive)
                .OrderByDescending(p => p.PaidAt)
                .ToListAsync(ct);

            // Expected revenue by examination type, based on (non-cancelled) appointments in the period
            var examStats = await _db.ReserveExaminationTypes
                .Where(rt => rt.Reserve.AppointmentAt >= fromDate && rt.Reserve.AppointmentAt < toExclusive)
                .Where(rt => rt.Reserve.Status == null || (rt.Reserve.Status.Name != StatusNames.Cancelled && rt.Reserve.Status.Name != StatusNames.NoShow))
                .GroupBy(rt => rt.ExaminationType.Name)
                .Select(g => new ExamTypeStat
                {
                    Name = g.Key,
                    Count = g.Count(),
                    Expected = g.Sum(x => x.Price)
                })
                .OrderByDescending(e => e.Expected)
                .ToListAsync(ct);

            // Per-doctor workload and expected revenue for appointments in the period.
            var doctorStats = (await _db.Reserves
                .Where(r => r.AppointmentAt >= fromDate && r.AppointmentAt < toExclusive && r.DoctorId != null)
                .Select(r => new
                {
                    Doctor = r.Doctor!.FullName,
                    Status = r.Status!.Name,
                    Total = r.ReserveExaminationTypes!.Sum(x => (decimal?)x.Price) ?? 0
                })
                .ToListAsync(ct))
                .GroupBy(r => r.Doctor)
                .Select(g => new DoctorStat
                {
                    Name = g.Key,
                    Booked = g.Count(x => x.Status != StatusNames.Cancelled),
                    Completed = g.Count(x => x.Status == StatusNames.Completed),
                    NoShow = g.Count(x => x.Status == StatusNames.NoShow),
                    Cancelled = g.Count(x => x.Status == StatusNames.Cancelled),
                    Revenue = g.Where(x => !StatusNames.IsClosedWithoutVisit(x.Status)).Sum(x => x.Total)
                })
                .OrderByDescending(d => d.Revenue)
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
                RefundTotal = -payments.Where(p => p.Amount < 0).Sum(p => p.Amount),
                ExamTypeStats = examStats,
                DoctorStats = doctorStats,
                DailyTotals = payments
                    .GroupBy(p => p.PaidAt.Date)
                    .Select(g => new DailyTotal { Date = g.Key, Count = g.Count(), Total = g.Sum(x => x.Amount) })
                    .OrderBy(d => d.Date)
                    .ToList()
            };

            return model;
        }
    }
}
