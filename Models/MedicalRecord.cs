namespace ClinicReservation.Models
{
    /// <summary>
    /// The doctor's note for one visit (elektron tibbi kart): complaint,
    /// history, findings, ICD-10 diagnosis, treatment and prescription.
    /// </summary>
    public class MedicalRecord : BaseModel
    {
        public int ReserveId { get; set; }
        public Reserve Reserve { get; set; } = null!;

        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;

        public int? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }

        /// <summary>Şikayətlər</summary>
        public string? Complaint { get; set; }
        /// <summary>Anamnez</summary>
        public string? Anamnesis { get; set; }
        /// <summary>Obyektiv müayinə</summary>
        public string? Examination { get; set; }

        /// <summary>BXT-10 (ICD-10) code, e.g. "J06.9".</summary>
        public string? IcdCode { get; set; }
        /// <summary>Diaqnoz</summary>
        public string? Diagnosis { get; set; }

        /// <summary>Müalicə planı</summary>
        public string? Treatment { get; set; }
        /// <summary>Resept (one medicine per line)</summary>
        public string? Prescription { get; set; }
        /// <summary>Tövsiyələr</summary>
        public string? Recommendations { get; set; }

        /// <summary>Suggested control visit in N days.</summary>
        public int? FollowUpDays { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
