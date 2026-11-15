using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicReservation.Models
{
    public enum StockItemKind
    {
        Medicine = 1,
        Consumable = 2,
        Reagent = 3,
        Other = 4
    }

    /// <summary>A medicine or material kept in stock (e.g. "Şpris 5 ml", "Deksametazon 4 mq/ml").</summary>
    public class StockItem : BaseModel
    {
        public string Name { get; set; } = "";
        /// <summary>Internal code or the package barcode (optional, unique).</summary>
        public string? Code { get; set; }
        public StockItemKind Kind { get; set; } = StockItemKind.Consumable;
        /// <summary>Dosage form / package, e.g. "ampul 2 ml", "tablet 500 mq".</summary>
        public string? Form { get; set; }
        /// <summary>Unit stock is counted in: ədəd, ampul, flakon, qutu …</summary>
        public string Unit { get; set; } = "ədəd";
        /// <summary>Below this (all warehouses together) the item shows as "az qalıb".</summary>
        public decimal MinQuantity { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Note { get; set; }

        public List<StockBatch> Batches { get; set; } = new();

        [NotMapped]
        public string DisplayName => string.IsNullOrWhiteSpace(Form) ? Name : $"{Name} ({Form})";
    }

    /// <summary>Anbar: the central store or a department's own cupboard.</summary>
    public class Warehouse : BaseModel
    {
        public string Name { get; set; } = "";
        /// <summary>Materials of this department's examinations are taken from here.</summary>
        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }
        /// <summary>The central store: receipts go here by default, and services without a department warehouse use it.</summary>
        public bool IsMain { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class Supplier : BaseModel
    {
        public string Name { get; set; } = "";
        public string? Voen { get; set; }
        public string? Phone { get; set; }
        public string? Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// A lot of one item in one warehouse (seriya + son istifadə tarixi + alış qiyməti).
    /// Quantity always equals the sum of the movements on it.
    /// </summary>
    public class StockBatch : BaseModel
    {
        public int StockItemId { get; set; }
        public StockItem StockItem { get; set; } = null!;
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; } = null!;

        public string? BatchNo { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public DateTime ReceivedAt { get; set; }

        /// <summary>PostgreSQL xmin — two people taking the same lot at once can't both succeed.</summary>
        [Timestamp]
        public uint Version { get; set; }
    }

    public enum StockDocType
    {
        /// <summary>Mədaxil — from a supplier.</summary>
        Receipt = 1,
        /// <summary>Köçürmə — between warehouses.</summary>
        Transfer = 2,
        /// <summary>Silinmə / buraxılış — expired, damaged, given to a patient, used.</summary>
        WriteOff = 3,
        /// <summary>Sərfiyyat — materials of a service, taken automatically.</summary>
        Consumption = 4,
        /// <summary>İnventarizasiya — counted quantity differs from the system.</summary>
        Count = 5
    }

    public enum StockDocStatus
    {
        Posted = 1,
        Cancelled = 2
    }

    public class StockDocument : BaseModel
    {
        public StockDocType Type { get; set; }
        public StockDocStatus Status { get; set; } = StockDocStatus.Posted;
        /// <summary>e.g. "MD-00012"; set right after the first save.</summary>
        public string Number { get; set; } = "";
        public DateTime Date { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }

        public int? FromWarehouseId { get; set; }
        public Warehouse? FromWarehouse { get; set; }
        public int? ToWarehouseId { get; set; }
        public Warehouse? ToWarehouse { get; set; }

        public int? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        /// <summary>Supplier's invoice (qaimə-faktura) number.</summary>
        public string? InvoiceNo { get; set; }

        /// <summary>Write-off reason, e.g. "Vaxtı keçib".</summary>
        public string? Reason { get; set; }
        public string? Note { get; set; }

        public int? PatientId { get; set; }
        public Patient? Patient { get; set; }
        public int? ReserveId { get; set; }
        public Reserve? Reserve { get; set; }
        /// <summary>Hospital stay the medicine was given in.</summary>
        public int? AdmissionId { get; set; }
        public Admission? Admission { get; set; }

        public DateTime? CancelledAt { get; set; }
        public string? CancelledBy { get; set; }
        public string? CancelReason { get; set; }

        public List<StockMovement> Movements { get; set; } = new();

        [NotMapped]
        public IEnumerable<StockMovement> Lines => Movements.Where(m => !m.IsReversal);

        /// <summary>Value of the document at purchase price (a transfer counts its incoming side only).</summary>
        [NotMapped]
        public decimal Value => Type == StockDocType.Transfer
            ? Lines.Where(m => m.Quantity > 0).Sum(m => m.Quantity * m.UnitCost)
            : Lines.Sum(m => Math.Abs(m.Quantity) * m.UnitCost);
    }

    /// <summary>One change on one lot: + comes in, − goes out.</summary>
    public class StockMovement : BaseModel
    {
        public int StockDocumentId { get; set; }
        public StockDocument StockDocument { get; set; } = null!;
        public int StockItemId { get; set; }
        public StockItem StockItem { get; set; } = null!;
        public int StockBatchId { get; set; }
        public StockBatch StockBatch { get; set; } = null!;
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; } = null!;

        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public DateTime At { get; set; }

        /// <summary>For a count: what was physically counted.</summary>
        public decimal? Counted { get; set; }
        /// <summary>Added when the document was cancelled (undoes a line).</summary>
        public bool IsReversal { get; set; }
    }

    /// <summary>Materials one examination uses (taken from stock when the visit is completed).</summary>
    public class ExaminationTypeStockItem
    {
        public int ExaminationTypeId { get; set; }
        public ExaminationType ExaminationType { get; set; } = null!;
        public int StockItemId { get; set; }
        public StockItem StockItem { get; set; } = null!;
        public decimal Quantity { get; set; }
    }

    public static class StockLabels
    {
        public static string Kind(StockItemKind k) => k switch
        {
            StockItemKind.Medicine => "Dərman",
            StockItemKind.Consumable => "Sərf materialı",
            StockItemKind.Reagent => "Reagent",
            _ => "Digər"
        };

        public static string Doc(StockDocType t) => t switch
        {
            StockDocType.Receipt => "Mədaxil",
            StockDocType.Transfer => "Köçürmə",
            StockDocType.WriteOff => "Silinmə",
            StockDocType.Consumption => "Xidmət sərfiyyatı",
            StockDocType.Count => "İnventarizasiya",
            _ => t.ToString()
        };

        public static string Prefix(StockDocType t) => t switch
        {
            StockDocType.Receipt => "MD",
            StockDocType.Transfer => "KC",
            StockDocType.WriteOff => "SL",
            StockDocType.Consumption => "SR",
            StockDocType.Count => "IN",
            _ => "SN"
        };

        public static string Icon(StockDocType t) => t switch
        {
            StockDocType.Receipt => "bi-box-arrow-in-down",
            StockDocType.Transfer => "bi-arrow-left-right",
            StockDocType.WriteOff => "bi-box-arrow-up",
            StockDocType.Consumption => "bi-clipboard2-minus",
            StockDocType.Count => "bi-list-check",
            _ => "bi-file-earmark"
        };

        public static readonly string[] Units = { "ədəd", "ampul", "flakon", "tablet", "qutu", "paket", "dəst", "cüt", "ml", "litr", "qram", "rulon" };

        public static readonly string[] WriteOffReasons =
            { "Xəstəyə verildi", "Prosedurda istifadə edildi", "Vaxtı keçib", "Zədələnib / sınıb", "Daxili istifadə", "Digər" };
    }
}
