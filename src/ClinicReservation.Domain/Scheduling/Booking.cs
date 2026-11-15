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

    public static class BookingLabels
    {
        public static string Source(ReserveSource s) => s switch
        {
            ReserveSource.Reception => "Registratura",
            ReserveSource.Phone => "Telefon",
            ReserveSource.Online => "Onlayn",
            ReserveSource.Referral => "Həkim göndərişi",
            ReserveSource.Campaign => "Kampaniya",
            _ => "Digər"
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
