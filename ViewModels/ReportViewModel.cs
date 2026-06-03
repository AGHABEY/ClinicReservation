using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
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

    public class ReportViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        // Financial summary (based on payments in the period)
        public decimal TotalIncome { get; set; }
        public decimal CashTotal { get; set; }
        public decimal CardTotal { get; set; }
        public int PaymentCount { get; set; }

        public List<DailyTotal> DailyTotals { get; set; } = new();
        public List<ExamTypeStat> ExamTypeStats { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();

        // Helpers for chart bars
        public decimal MaxDaily => DailyTotals.Count > 0 ? DailyTotals.Max(d => d.Total) : 0;
    }
}
