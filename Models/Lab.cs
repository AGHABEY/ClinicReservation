using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicReservation.Models
{
    public enum LabResultType
    {
        /// <summary>A number compared with the reference range (e.g. HGB 135 g/L).</summary>
        Numeric = 1,
        /// <summary>One of a fixed list (e.g. Mənfi / Müsbət); anything but the normal value is flagged.</summary>
        Choice = 2,
        /// <summary>Free text, not flagged automatically.</summary>
        Text = 3
    }

    /// <summary>One analysis parameter in the lab catalog (e.g. Hemoqlobin, Qlükoza).</summary>
    public class LabTest : BaseModel
    {
        /// <summary>Short code printed on the report, e.g. "HGB".</summary>
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Report section, e.g. "Ümumi qan analizi", "Biokimya".</summary>
        public string? Category { get; set; }
        /// <summary>Qan, Sidik, ...</summary>
        public string SampleType { get; set; } = "Qan";
        public string? Unit { get; set; }

        public LabResultType ResultType { get; set; } = LabResultType.Numeric;
        /// <summary>For Choice: allowed values separated by ";".</summary>
        public string? Choices { get; set; }

        /// <summary>Adult reference range (for everyone, or men when a separate female range is set).</summary>
        public decimal? RefMin { get; set; }
        public decimal? RefMax { get; set; }
        /// <summary>Optional separate range for women.</summary>
        public decimal? RefMinFemale { get; set; }
        public decimal? RefMaxFemale { get; set; }
        /// <summary>For Choice/Text: the normal value or a reference description.</summary>
        public string? RefText { get; set; }

        /// <summary>Panic values — shown in red and need confirmation.</summary>
        public decimal? CriticalMin { get; set; }
        public decimal? CriticalMax { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public List<ExaminationTypeLabTest> ExaminationTypes { get; set; } = new();

        [NotMapped]
        public string[] ChoiceList => (Choices ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Which lab tests an orderable examination (with its price) contains.</summary>
    public class ExaminationTypeLabTest
    {
        public int ExaminationTypeId { get; set; }
        public ExaminationType ExaminationType { get; set; } = null!;
        public int LabTestId { get; set; }
        public LabTest LabTest { get; set; } = null!;
    }

    public enum LabOrderStatus
    {
        /// <summary>Sample taken, results being entered.</summary>
        Collected = 1,
        /// <summary>Results checked and released by the lab.</summary>
        Validated = 2,
        Cancelled = 3
    }

    /// <summary>One sample (tube) with a barcode and the tests run on it.</summary>
    public class LabOrder : BaseModel
    {
        public int ReserveId { get; set; }
        public Reserve Reserve { get; set; } = null!;
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;

        /// <summary>10-digit barcode: yyMMdd + running number.</summary>
        public string Barcode { get; set; } = "";
        public LabOrderStatus Status { get; set; } = LabOrderStatus.Collected;

        public DateTime CollectedAt { get; set; }
        public string? CollectedBy { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public string? ValidatedBy { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? Note { get; set; }

        public List<LabResult> Results { get; set; } = new();

        [NotMapped]
        public bool HasAbnormal => Results.Any(r => r.Flag is not (LabFlag.None or LabFlag.Normal));
        [NotMapped]
        public bool HasCritical => Results.Any(r => r.Flag is LabFlag.CriticalLow or LabFlag.CriticalHigh);
    }

    public enum LabFlag
    {
        None = 0,
        Normal = 1,
        Low = 2,
        High = 3,
        CriticalLow = 4,
        CriticalHigh = 5,
        /// <summary>Choice result other than the normal value.</summary>
        Abnormal = 6
    }

    public class LabResult : BaseModel
    {
        public int LabOrderId { get; set; }
        public LabOrder LabOrder { get; set; } = null!;
        public int LabTestId { get; set; }
        public LabTest LabTest { get; set; } = null!;

        public string? Value { get; set; }
        public decimal? NumericValue { get; set; }
        public LabFlag Flag { get; set; }

        /// <summary>Unit and reference as they were when the result was entered.</summary>
        public string? Unit { get; set; }
        public string? Reference { get; set; }

        public DateTime? EnteredAt { get; set; }
        public string? EnteredBy { get; set; }
    }
}
