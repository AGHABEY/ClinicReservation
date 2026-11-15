using ClinicReservation.Models;

namespace ClinicReservation.ViewModels
{
    public class LabPendingRow
    {
        public Reserve Reserve { get; set; } = null!;
        public List<LabTest> Tests { get; set; } = new();
    }

    public class LabWorklistViewModel
    {
        public string Tab { get; set; } = "pending";
        public DateTime Date { get; set; }
        public string? Query { get; set; }
        public List<LabPendingRow> Pending { get; set; } = new();
        public List<LabOrder> Orders { get; set; } = new();
        public int PendingCount { get; set; }
        public int WorkCount { get; set; }
        public int DoneCount { get; set; }
    }

    public class LabResultInput
    {
        public int Id { get; set; }
        public string? Value { get; set; }
    }

    public class LabOrderViewModel
    {
        public LabOrder Order { get; set; } = null!;
        public Dictionary<int, LabResult> Previous { get; set; } = new();
        public bool CanEdit { get; set; }
    }

    public class LabSaveInput
    {
        public int OrderId { get; set; }
        public List<LabResultInput> Results { get; set; } = new();
        public string? Note { get; set; }
        /// <summary>"1" = save and validate.</summary>
        public string? Validate { get; set; }
        public bool SendSms { get; set; }
        public bool ConfirmCritical { get; set; }
    }
}
