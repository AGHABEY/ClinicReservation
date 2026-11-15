using ClinicReservation.Models;

namespace ClinicReservation.Modules.Billing
{
    public class DailyTotal
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
        public decimal Total { get; set; }
    }

    public class ExamTypeStat
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public decimal Expected { get; set; }
    }

    public class DoctorStat
    {
        public string Name { get; set; } = "";
        public int Booked { get; set; }
        public int Completed { get; set; }
        public int NoShow { get; set; }
        public int Cancelled { get; set; }
        public decimal Revenue { get; set; }
        public int NoShowPercent => Booked == 0 ? 0 : (int)Math.Round(NoShow * 100.0 / Booked);
    }

    public class ReportViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        // Financial summary (based on payments in the period)
        public decimal TotalIncome { get; set; }
        public decimal CashTotal { get; set; }
        public decimal CardTotal { get; set; }
        /// <summary>Paid with loyalty points (not money).</summary>
        public decimal PointsTotal { get; set; }
        public int PaymentCount { get; set; }
        public decimal RefundTotal { get; set; }

        public List<DailyTotal> DailyTotals { get; set; } = new();
        public List<ExamTypeStat> ExamTypeStats { get; set; } = new();
        public List<DoctorStat> DoctorStats { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();

        // Helpers for chart bars
        public decimal MaxDaily => DailyTotals.Count > 0 ? DailyTotals.Max(d => Math.Abs(d.Total)) : 0;
    }
}
