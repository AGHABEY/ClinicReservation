namespace ClinicReservation.Models
{
    /// <summary>Where a line's discount came from. Only one applies per line: the best one (they don't add up).</summary>
    public enum DiscountKind { FollowUp = 1, Campaign = 2, Promo = 3, Loyalty = 4, Social = 5, Corporate = 6, Manual = 7 }

    public enum DiscountType { Percent = 1, Amount = 2 }

    /// <summary>Card level, reached automatically by what the card's members paid in the last 12 months.</summary>
    public class LoyaltyTier : BaseModel
    {
        public string Name { get; set; } = "";
        /// <summary>Paid in 12 months to reach it (₼).</summary>
        public decimal MinSpend { get; set; }
        public decimal DiscountPercent { get; set; }
        /// <summary>Points earned, as % of what is paid (1 bal = PointValue ₼).</summary>
        public decimal PointsPercent { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Loyallıq kartı. A family card has several members who share the points and the level.</summary>
    public class LoyaltyCard : BaseModel
    {
        public string Number { get; set; } = "";
        public int HolderPatientId { get; set; }
        public Patient Holder { get; set; } = null!;
        public bool IsFamily { get; set; }
        /// <summary>Fixed level (e.g. VIP, staff); null = by spending.</summary>
        public int? TierOverrideId { get; set; }
        public LoyaltyTier? TierOverride { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public List<LoyaltyCardMember> Members { get; set; } = new();
    }

    public class LoyaltyCardMember : BaseModel
    {
        public int CardId { get; set; }
        public LoyaltyCard Card { get; set; } = null!;
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public DateTime JoinedAt { get; set; }
    }

    public enum PointsKind { Earn = 1, Spend = 2, Adjust = 3 }

    /// <summary>Points ledger of a card; the balance is the sum. One row per payment (PaymentKey) at most.</summary>
    public class LoyaltyTransaction : BaseModel
    {
        public int CardId { get; set; }
        public LoyaltyCard Card { get; set; } = null!;
        public int? PatientId { get; set; }
        public int? ReserveId { get; set; }
        public Guid? PaymentKey { get; set; }
        public PointsKind Kind { get; set; }
        public int Points { get; set; }
        /// <summary>Money paid (cash/card) counted toward the level; negative for refunds.</summary>
        public decimal Spend { get; set; }
        public DateTime At { get; set; }
        public string? By { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>Kampaniya (automatic) or promo code (Code set). Applies to a department's services or chosen ones.</summary>
    public class Campaign : BaseModel
    {
        public string Name { get; set; } = "";
        /// <summary>Null = applies by itself; set = only when the code is given at booking.</summary>
        public string? Code { get; set; }
        public DiscountType Type { get; set; } = DiscountType.Percent;
        /// <summary>Percent, or ₼ off each matching service.</summary>
        public decimal Value { get; set; }
        public DateTime StartsOn { get; set; }
        public DateTime EndsOn { get; set; }
        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public List<CampaignExam> Exams { get; set; } = new();
        /// <summary>Visits it may be used in (all patients); null = no limit.</summary>
        public int? MaxUses { get; set; }
        public int? MaxUsesPerPatient { get; set; }
        public bool NewPatientsOnly { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Note { get; set; }
    }

    public class CampaignExam
    {
        public int CampaignId { get; set; }
        public Campaign Campaign { get; set; } = null!;
        public int ExaminationTypeId { get; set; }
        public ExaminationType ExaminationType { get; set; } = null!;
    }

    /// <summary>Sosial güzəşt kataloqu: veteran, əlillik, təqaüdçü… with the discount it gives.</summary>
    public class BenefitCategory : BaseModel
    {
        public string Name { get; set; } = "";
        public decimal Percent { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Note { get; set; }
    }

    /// <summary>A patient's social discount, backed by a document.</summary>
    public class PatientBenefit : BaseModel
    {
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public int CategoryId { get; set; }
        public BenefitCategory Category { get; set; } = null!;
        public string DocumentNo { get; set; } = "";
        /// <summary>Null = no end date (e.g. permanent disability).</summary>
        public DateTime? ValidUntil { get; set; }
        public DateTime AddedAt { get; set; }
        public string? AddedBy { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Korporativ müştəri: a company whose employees get the contract discount.</summary>
    public class Company : BaseModel
    {
        public string Name { get; set; } = "";
        public string? ContractNo { get; set; }
        public decimal DiscountPercent { get; set; }
        public DateTime? ContractFrom { get; set; }
        public DateTime? ContractUntil { get; set; }
        public string? Contact { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PatientCompany : BaseModel
    {
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;
        public string? EmployeeNo { get; set; }
        public DateTime AddedAt { get; set; }
    }

    /// <summary>Loyalty settings, appsettings.json → "Loyalty".</summary>
    public class LoyaltyOptions
    {
        /// <summary>₼ per point: 0.01 = 1 bal is 1 qəpik.</summary>
        public decimal PointValue { get; set; } = 0.01m;
        /// <summary>Fewest points that can be spent at once.</summary>
        public int MinPointsToSpend { get; set; } = 100;
    }

    public static class DiscountLabels
    {
        public static string Kind(DiscountKind? k) => k switch
        {
            DiscountKind.FollowUp => "Kontrol qəbulu",
            DiscountKind.Campaign => "Kampaniya",
            DiscountKind.Promo => "Promo kod",
            DiscountKind.Loyalty => "Loyallıq kartı",
            DiscountKind.Social => "Sosial güzəşt",
            DiscountKind.Corporate => "Korporativ",
            DiscountKind.Manual => "Əl ilə endirim",
            _ => "—"
        };
    }
}
