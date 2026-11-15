namespace ClinicReservation.Models
{
    /// <summary>
    /// An event one module raised for others, stored in the same transaction as the change that
    /// caused it (so it can't be lost), then handled and marked processed. Failed handling is retried.
    /// </summary>
    public class OutboxMessage
    {
        public long Id { get; set; }
        /// <summary>Event type name, e.g. "VisitCompleted".</summary>
        public string Type { get; set; } = "";
        public string Payload { get; set; } = "";
        public DateTime OccurredAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
    }
}
