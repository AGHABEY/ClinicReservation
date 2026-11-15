using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicReservation.Models
{
    /// <summary>
    /// A patient's card. One record per person (identified by FİN); every
    /// reservation points to it, so visit history lives in one place.
    /// </summary>
    public class Patient : BaseModel
    {
        public string Name { get; set; } = "";
        public string Surname { get; set; } = "";

        /// <summary>FİN — stored trimmed and upper-case, unique.</summary>
        public string PinCode { get; set; } = "";

        public string Phone { get; set; } = "";
        public DateTime Birthday { get; set; }

        /// <summary>Free-form note for staff (e.g. "prefers morning appointments").</summary>
        public string? Notes { get; set; }

        /// <summary>Known allergies — shown as a red warning to doctors.</summary>
        public string? Allergies { get; set; }

        /// <summary>Chronic conditions (e.g. diabetes, hypertension).</summary>
        public string? ChronicConditions { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<Reserve> Reserves { get; set; } = new();
        public List<MedicalRecord> MedicalRecords { get; set; } = new();

        [NotMapped]
        public bool HasMedicalAlerts => !string.IsNullOrWhiteSpace(Allergies) || !string.IsNullOrWhiteSpace(ChronicConditions);

        [NotMapped]
        public string FullName => $"{Name} {Surname}";

        public static string NormalizePin(string? pin) => (pin ?? "").Trim().ToUpperInvariant();
    }
}
