using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicReservation.Models
{
    public enum WardGender
    {
        Mixed = 0,
        Male = 1,
        Female = 2
    }

    /// <summary>Palata: a room with beds in a department.</summary>
    public class Ward : BaseModel
    {
        public string Name { get; set; } = "";
        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }
        /// <summary>Kişi / qadın palatası — only a warning when a patient of the other sex is placed.</summary>
        public WardGender Gender { get; set; }
        /// <summary>Price of one bed-day in this ward (shown on the bill summary).</summary>
        public decimal DailyPrice { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public List<Bed> Beds { get; set; } = new();
    }

    public class Bed : BaseModel
    {
        public int WardId { get; set; }
        public Ward Ward { get; set; } = null!;
        /// <summary>e.g. "1", "2A".</summary>
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;

        [NotMapped]
        public string Label => $"{Ward?.Name} · çarpayı {Name}";
    }

    public enum AdmissionStatus
    {
        Active = 1,
        Discharged = 2,
        /// <summary>Entered by mistake; frees the bed, no epicrisis.</summary>
        Cancelled = 3
    }

    public enum AdmissionType
    {
        Planned = 1,
        Emergency = 2
    }

    public enum DischargeOutcome
    {
        Recovered = 1,
        Improved = 2,
        NoChange = 3,
        Worsened = 4,
        TransferredOut = 5,
        SelfDischarge = 6,
        Died = 7
    }

    /// <summary>One hospital stay (xəstəlik tarixi).</summary>
    public class Admission : BaseModel
    {
        /// <summary>e.g. "ST-00012", set after the first save.</summary>
        public string Number { get; set; } = "";
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public int? BedId { get; set; }
        public Bed? Bed { get; set; }
        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }
        /// <summary>Müalicə həkimi.</summary>
        public int? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }
        /// <summary>The outpatient visit that referred the patient (optional).</summary>
        public int? ReserveId { get; set; }
        public Reserve? Reserve { get; set; }

        public AdmissionType Type { get; set; } = AdmissionType.Planned;
        public AdmissionStatus Status { get; set; } = AdmissionStatus.Active;

        public DateTime AdmittedAt { get; set; }
        public string? AdmittedBy { get; set; }
        public string? AdmitIcdCode { get; set; }
        public string? AdmitDiagnosis { get; set; }
        /// <summary>Daxilolma səbəbi / şikayətlər.</summary>
        public string? AdmitNote { get; set; }

        // ----- Discharge (epikriz) -----
        public DateTime? DischargedAt { get; set; }
        public string? DischargedBy { get; set; }
        public DischargeOutcome? Outcome { get; set; }
        public string? FinalIcdCode { get; set; }
        public string? FinalDiagnosis { get; set; }
        /// <summary>Xəstəliyin gedişi.</summary>
        public string? Course { get; set; }
        /// <summary>Aparılmış müalicə.</summary>
        public string? TreatmentSummary { get; set; }
        public string? Recommendations { get; set; }
        public string? CancelReason { get; set; }

        public List<BedStay> Stays { get; set; } = new();
        public List<Observation> Observations { get; set; } = new();
        public List<MedicationOrder> Orders { get; set; } = new();

        [NotMapped]
        public bool IsActive => Status == AdmissionStatus.Active;

        /// <summary>Hospital day number: admission day is day 1.</summary>
        public int Day(DateTime now) => ((DischargedAt ?? now).Date - AdmittedAt.Date).Days + 1;
    }

    /// <summary>Which bed the patient was in, and when (a transfer closes one and opens another).</summary>
    public class BedStay : BaseModel
    {
        public int AdmissionId { get; set; }
        public Admission Admission { get; set; } = null!;
        public int BedId { get; set; }
        public Bed Bed { get; set; } = null!;
        public DateTime From { get; set; }
        public DateTime? To { get; set; }
        /// <summary>Bed-day price at the time (from the ward).</summary>
        public decimal DailyPrice { get; set; }
    }

    public enum ObservationKind
    {
        /// <summary>Həkim gündəliyi.</summary>
        Doctor = 1,
        /// <summary>Tibb bacısı qeydi / vital göstəricilər.</summary>
        Nurse = 2
    }

    public class Observation : BaseModel
    {
        public int AdmissionId { get; set; }
        public Admission Admission { get; set; } = null!;
        public ObservationKind Kind { get; set; }
        public DateTime At { get; set; }
        public string? Author { get; set; }
        public string? Text { get; set; }

        // Vital signs (all optional)
        public decimal? Temperature { get; set; }
        public int? Pulse { get; set; }
        public int? BpSystolic { get; set; }
        public int? BpDiastolic { get; set; }
        public int? RespRate { get; set; }
        public int? SpO2 { get; set; }

        [NotMapped]
        public bool HasVitals => Temperature != null || Pulse != null || BpSystolic != null || RespRate != null || SpO2 != null;
    }

    /// <summary>Dərman təyinatı. Doses are scheduled at the given times every day between start and end.</summary>
    public class MedicationOrder : BaseModel
    {
        public int AdmissionId { get; set; }
        public Admission Admission { get; set; } = null!;
        /// <summary>When set, each given dose is taken from stock.</summary>
        public int? StockItemId { get; set; }
        public StockItem? StockItem { get; set; }
        /// <summary>Name when the drug isn't a stock item (or as written by the doctor).</summary>
        public string DrugName { get; set; } = "";
        /// <summary>Doza, e.g. "4 mq", "1 tablet".</summary>
        public string? Dose { get; set; }
        /// <summary>Stock units used per dose (e.g. 1 ampul).</summary>
        public decimal QuantityPerDose { get; set; } = 1;
        public string? Route { get; set; }
        /// <summary>"08:00;20:00". Empty = lazım olduqda (PRN).</summary>
        public string? Times { get; set; }
        public DateTime StartAt { get; set; }
        /// <summary>Last day doses are given (inclusive); null = until stopped.</summary>
        public DateTime? EndDate { get; set; }
        public string? Note { get; set; }
        public DateTime OrderedAt { get; set; }
        public string? OrderedBy { get; set; }
        public DateTime? StoppedAt { get; set; }
        public string? StoppedBy { get; set; }

        public List<MedicationDose> Doses { get; set; } = new();

        [NotMapped]
        public bool IsPrn => string.IsNullOrWhiteSpace(Times);

        [NotMapped]
        public TimeSpan[] TimeList => (Times ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => TimeSpan.TryParse(t, out var ts) ? ts : (TimeSpan?)null)
            .Where(t => t != null).Select(t => t!.Value).OrderBy(t => t).ToArray();

        /// <summary>Scheduled moments on a day (nothing for PRN orders, stopped or finished orders).</summary>
        public IEnumerable<DateTime> SlotsOn(DateTime day)
        {
            day = day.Date;
            if (IsPrn || day < StartAt.Date || (EndDate != null && day > EndDate.Value.Date)) yield break;
            foreach (var t in TimeList)
            {
                var slot = day + t;
                if (slot < StartAt.AddMinutes(-59)) continue; // a dose due within the hour of ordering still counts
                if (StoppedAt != null && slot >= StoppedAt) continue;
                yield return slot;
            }
        }

        [NotMapped]
        public string Name => StockItem?.DisplayName ?? DrugName;

        public bool IsActiveOn(DateTime day) =>
            StartAt.Date <= day.Date && (EndDate == null || EndDate.Value.Date >= day.Date)
            && (StoppedAt == null || StoppedAt.Value.Date >= day.Date);
    }

    public enum DoseStatus
    {
        Given = 1,
        /// <summary>Verilmədi (with a reason: xəstə imtina etdi, müayinədə idi …).</summary>
        Skipped = 2
    }

    public class MedicationDose : BaseModel
    {
        public int MedicationOrderId { get; set; }
        public MedicationOrder MedicationOrder { get; set; } = null!;
        /// <summary>The slot this dose belongs to (for PRN: when it was given).</summary>
        public DateTime ScheduledAt { get; set; }
        public DoseStatus Status { get; set; }
        public DateTime RecordedAt { get; set; }
        public string? RecordedBy { get; set; }
        public string? Note { get; set; }
        /// <summary>Stock write-off made for this dose.</summary>
        public int? StockDocumentId { get; set; }
    }

    public static class InpatientLabels
    {
        public static string Outcome(DischargeOutcome? o) => o switch
        {
            DischargeOutcome.Recovered => "Sağalma",
            DischargeOutcome.Improved => "Yaxşılaşma",
            DischargeOutcome.NoChange => "Dəyişiklik yoxdur",
            DischargeOutcome.Worsened => "Pisləşmə",
            DischargeOutcome.TransferredOut => "Başqa müəssisəyə köçürülüb",
            DischargeOutcome.SelfDischarge => "Öz istəyi ilə",
            DischargeOutcome.Died => "Ölüm",
            _ => "—"
        };

        public static string Type(AdmissionType t) => t == AdmissionType.Emergency ? "Təcili" : "Planlı";

        public static string Gender(WardGender g) => g switch
        {
            WardGender.Male => "Kişi",
            WardGender.Female => "Qadın",
            _ => "Qarışıq"
        };

        public static readonly string[] Routes =
            { "Venadaxili", "Venadaxili damcı", "Əzələdaxili", "Dərialtı", "Per os", "Dilaltı", "İnhalyasiya", "Rektal", "Xarici", "Göz damcısı" };

        /// <summary>Common schedules for the order form.</summary>
        public static readonly (string Label, string Times)[] Schedules =
        {
            ("Gündə 1 dəfə (08:00)", "08:00"),
            ("Gündə 2 dəfə (08:00, 20:00)", "08:00;20:00"),
            ("Gündə 3 dəfə (08:00, 14:00, 20:00)", "08:00;14:00;20:00"),
            ("Gündə 4 dəfə (06:00, 12:00, 18:00, 22:00)", "06:00;12:00;18:00;22:00"),
            ("Gecə (22:00)", "22:00"),
            ("Lazım olduqda", ""),
        };

        public static readonly string[] SkipReasons =
            { "Xəstə imtina etdi", "Xəstə müayinədə / əməliyyatda idi", "Həkim göstərişi ilə dayandırıldı", "Dərman yoxdur", "Digər" };
    }
}
