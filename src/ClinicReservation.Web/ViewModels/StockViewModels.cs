using ClinicReservation.Models;
using ClinicReservation.Services;

namespace ClinicReservation.ViewModels
{
    public enum StockState { Ok, Low, Out, Expiring, Expired }

    public class StockBalanceRow
    {
        public StockItem Item { get; set; } = null!;
        public decimal Quantity { get; set; }
        /// <summary>Part of the quantity that is already past its expiry date.</summary>
        public decimal ExpiredQuantity { get; set; }
        public DateTime? NearestExpiry { get; set; }
        public decimal Value { get; set; }
        public StockState State { get; set; }
        public decimal Usable => Quantity - ExpiredQuantity;
    }

    public class StockIndexViewModel
    {
        public List<StockBalanceRow> Rows { get; set; } = new();
        public List<Warehouse> Warehouses { get; set; } = new();
        public int? WarehouseId { get; set; }
        public StockItemKind? Kind { get; set; }
        public string? Query { get; set; }
        /// <summary>all | low | expiry</summary>
        public string Filter { get; set; } = "all";
        public int LowCount { get; set; }
        public int ExpiringCount { get; set; }
        public int ExpiredCount { get; set; }
        public int ItemCount { get; set; }
        public decimal TotalValue { get; set; }
        public bool ShowCosts { get; set; }
    }

    public class StockExpiryViewModel
    {
        public List<StockBatch> Lots { get; set; } = new();
        public List<Warehouse> Warehouses { get; set; } = new();
        public int? WarehouseId { get; set; }
        public int Days { get; set; } = StockService.ExpirySoonDays;
        public bool ShowCosts { get; set; }
    }

    public class StockItemDetailsViewModel
    {
        public StockItem Item { get; set; } = null!;
        public List<StockBatch> Lots { get; set; } = new();
        public List<StockMovement> Movements { get; set; } = new();
        public List<ExaminationType> UsedIn { get; set; } = new();
        public bool ShowCosts { get; set; }
    }

    public class StockLineInput
    {
        public int? ItemId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitCost { get; set; }
        public string? BatchNo { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    public class StockDocFormViewModel
    {
        public StockDocType Type { get; set; }
        /// <summary>Receipt: target; transfer: source; write-off: source.</summary>
        public int? WarehouseId { get; set; }
        /// <summary>Transfer target.</summary>
        public int? ToWarehouseId { get; set; }
        public int? SupplierId { get; set; }
        public string? InvoiceNo { get; set; }
        public DateTime Date { get; set; } = ClinicTime.Today;
        public string? Reason { get; set; }
        public string? Note { get; set; }
        /// <summary>Write-off to a patient: their FİN.</summary>
        public string? PatientPin { get; set; }
        public int? ReserveId { get; set; }
        public List<StockLineInput> Lines { get; set; } = new();

        // Display only
        public List<StockItem> Items { get; set; } = new();
        public List<Warehouse> Warehouses { get; set; } = new();
        public List<Supplier> Suppliers { get; set; } = new();
        /// <summary>warehouseId → itemId → usable quantity (for the "qalıq" hint next to each line).</summary>
        public Dictionary<int, Dictionary<int, decimal>> OnHand { get; set; } = new();
        public Reserve? Reserve { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class StockCountViewModel
    {
        public int? WarehouseId { get; set; }
        public List<Warehouse> Warehouses { get; set; } = new();
        public List<StockBatch> Lots { get; set; } = new();
        public string? Note { get; set; }
        public Dictionary<int, decimal?> Counted { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class StockDocListViewModel
    {
        public List<StockDocument> Documents { get; set; } = new();
        public StockDocType? Type { get; set; }
        public int? WarehouseId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<Warehouse> Warehouses { get; set; } = new();
        public bool ShowCosts { get; set; }
    }

    public class StockReportViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int? WarehouseId { get; set; }
        public List<Warehouse> Warehouses { get; set; } = new();
        public List<StockService.MovementRow> Rows { get; set; } = new();
        public bool ShowCosts { get; set; }
    }

    public class MaterialInput
    {
        public int? StockItemId { get; set; }
        public decimal? Quantity { get; set; }
    }
}
