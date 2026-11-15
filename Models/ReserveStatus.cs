namespace ClinicReservation.Models
{
    public class ReserveStatus : BaseModel
    {
        public string Name { get; set; } = "";

        public List<Reserve> Reserves { get; set; } = new();
    }

    /// <summary>Names of the rows seeded into the ReserveStatuses table.</summary>
    public static class StatusNames
    {
        public const string Waiting = "Waiting";       // Gözləyir
        public const string Confirmed = "Confirmed";   // Təsdiqləndi
        public const string Arrived = "Arrived";       // Gəldi (check-in)
        public const string InProgress = "InProgress"; // Qəbulda
        public const string Completed = "Completed";   // Tamamlandı
        public const string NoShow = "NoShow";         // Gəlmədi
        public const string Cancelled = "Cancelled";   // Ləğv edildi

        public static readonly string[] All = { Waiting, Confirmed, Arrived, InProgress, Completed, NoShow, Cancelled };

        public static string Label(string? name) => name switch
        {
            Waiting => "Gözləyir",
            Confirmed => "Təsdiqləndi",
            Arrived => "Gəldi",
            InProgress => "Qəbulda",
            Completed => "Tamamlandı",
            NoShow => "Gəlmədi",
            Cancelled => "Ləğv edildi",
            _ => name ?? "—"
        };

        /// <summary>Statuses where nothing is owed and the slot is free again.</summary>
        public static bool IsClosedWithoutVisit(string? name) => name is Cancelled or NoShow;
    }
}
