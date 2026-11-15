using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
{
    public static class PayStatus
    {
        public const string Unpaid = "Unpaid";
        public const string Partial = "Partial";
        public const string Paid = "Paid";
        public const string Refund = "Refund";       // paid more than is due (e.g. cancelled) — money must go back
        public const string Cancelled = "Cancelled"; // cancelled and nothing to settle
    }

    public class KassaItemViewModel
    {
        public Reserve Reserve { get; set; } = null!;
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance => Total - Paid;

        public string PaymentStatus =>
            Balance < 0 ? PayStatus.Refund
            : StatusNames.IsClosedWithoutVisit(Reserve.Status?.Name) ? PayStatus.Cancelled
            : Paid <= 0 ? PayStatus.Unpaid
            : Paid >= Total ? PayStatus.Paid
            : PayStatus.Partial;
    }

    public class KassaListViewModel
    {
        public List<KassaItemViewModel> Items { get; set; } = new();
        public decimal TotalExpected { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public decimal TotalToRefund { get; set; }
        public string? Filter { get; set; }
        public string? Query { get; set; }
    }

    public class PaymentViewModel
    {
        // Display
        public Reserve? Reserve { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance { get; set; }
        public List<Payment> Payments { get; set; } = new();

        // Form input
        public int ReserveId { get; set; }
        public decimal Amount { get; set; }
        public PaymentMethod Method { get; set; }
        public string? Note { get; set; }
    }
}
