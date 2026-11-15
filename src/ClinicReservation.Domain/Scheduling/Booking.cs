namespace ClinicReservation.Models
{
    /// <summary>How the booking reached the clinic (for reports and campaigns).</summary>
    public enum ReserveSource
    {
        Reception = 1,
        Phone = 2,
        Online = 3,
        Referral = 4,
        Campaign = 5,
        Other = 6
    }

    /// <summary>Who called the visit off.</summary>
    public enum CancelledBy
    {
        Patient = 1,
        Clinic = 2,
        Doctor = 3
    }

    /// <summary>Ləğv səbəbi — a catalog, so cancellations can be counted by reason.</summary>
    public class CancellationReason : BaseModel
    {
        public string Name { get; set; } = "";
        /// <summary>Who usually cancels for this reason (pre-selected on the form).</summary>
        public CancelledBy Party { get; set; } = CancelledBy.Patient;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Kurs: a set of visits booked together (e.g. 10 physiotherapy sessions, Mon/Wed/Fri 10:00).</summary>
    public class ReserveSeries : BaseModel
    {
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public int? DoctorId { get; set; }
        /// <summary>As the user asked for it, e.g. "10 seans · B.e, Ç, C · 10:00".</summary>
        public string Pattern { get; set; } = "";
        public int Count { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public List<Reserve> Reserves { get; set; } = new();
    }

    public static class BookingLabels
    {
        public static string Source(ReserveSource s) => s switch
        {
            ReserveSource.Reception => "Registratura",
            ReserveSource.Phone => "Telefon",
            ReserveSource.Online => "Onlayn",
            ReserveSource.Referral => "Həkim göndərişi",
            ReserveSource.Campaign => "Kampaniya",
            ReserveSource.Other => "Digər",
            _ => "Qeyd olunmayıb" // bookings made before the source was recorded
        };

        public static string Party(CancelledBy? p) => p switch
        {
            CancelledBy.Patient => "Xəstə",
            CancelledBy.Clinic => "Klinika",
            CancelledBy.Doctor => "Həkim",
            _ => "—"
        };

        /// <summary>Starter reasons, added once when the catalog is empty.</summary>
        public static readonly (string Name, CancelledBy Party)[] DefaultReasons =
        {
            ("Xəstə gələ bilmir", CancelledBy.Patient),
            ("Xəstə vaxtı dəyişmək istəyir", CancelledBy.Patient),
            ("Xəstə başqa klinikaya üz tutdu", CancelledBy.Patient),
            ("Xəstənin vəziyyəti dəyişdi (lazım deyil)", CancelledBy.Patient),
            ("Həkim işdə deyil", CancelledBy.Doctor),
            ("Həkimin təcili işi", CancelledBy.Doctor),
            ("Səhv yazılış", CancelledBy.Clinic),
            ("Avadanlıq nasazdır", CancelledBy.Clinic),
            ("Digər", CancelledBy.Patient),
        };
    }
}
