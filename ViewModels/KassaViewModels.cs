using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
{
    public class KassaItemViewModel
    {
        public Reserve Reserve { get; set; } = null!;
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance => Total - Paid;

        // Unpaid / Partial / Paid
        public string PaymentStatus =>
            Paid <= 0 ? "Unpaid" : (Paid >= Total ? "Paid" : "Partial");
    }

    public class KassaListViewModel
    {
        public List<KassaItemViewModel> Items { get; set; } = new();
        public decimal TotalExpected { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public string? Filter { get; set; }
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
