namespace ClinicReservation.Events
{
    /// <summary>Something that happened in one module that other modules may react to.</summary>
    public interface IIntegrationEvent { }

    /// <summary>A visit was completed (Scheduling, EMR) — its services' materials can leave stock.</summary>
    public record VisitCompleted(int ReserveId, string? User) : IIntegrationEvent;

    /// <summary>A lab sample was taken (Lab) — the lab services' tubes and reagents can leave stock.</summary>
    public record LabSampleCollected(int ReserveId, string? User) : IIntegrationEvent;

    /// <summary>A nurse gave a dose of a stock medicine (Inpatient) — it leaves the ward's store.</summary>
    /// <remarks>The dose is identified by its order and slot (unique), as its id doesn't exist before the save.</remarks>
    public record DoseGiven(int OrderId, DateTime ScheduledAt, int AdmissionId, int PatientId, int? DepartmentId, int StockItemId,
        decimal Quantity, string Note, string? User) : IIntegrationEvent;

    /// <summary>A freed time was offered to a waiting patient (Scheduling) — text them if they asked.</summary>
    public record WaitlistOffered(int EntryId, int PatientId, DateTime Slot, string DoctorName, string? User) : IIntegrationEvent;

    /// <summary>The doctor set (or cleared, DueDate null) the follow-up of a visit in its record (Emr → Scheduling).</summary>
    public record FollowUpPlanned(int ReserveId, int PatientId, int? DoctorId, DateTime? DueDate, string? User) : IIntegrationEvent;

    /// <summary>Reception asked to remind the patient of the follow-up (Scheduling → Notifications).</summary>
    public record FollowUpReminder(int FollowUpId, int PatientId, DateTime DueDate, string DoctorName, string? User) : IIntegrationEvent;

    /// <summary>Money (or points) taken or given back at the cashier (Billing → Loyalty). Amount &lt; 0 = refund.</summary>
    public record PaymentRecorded(Guid PaymentKey, int ReserveId, int PatientId, decimal Amount, Models.PaymentMethod Method, string? User) : IIntegrationEvent;

    /// <summary>Reacts to one event type. Returns a warning for the user (e.g. a stock shortage) or null.</summary>
    public interface IEventHandler
    {
        Type EventType { get; }
        Task<string?> HandleAsync(IIntegrationEvent e, CancellationToken ct);
    }

    public abstract class EventHandler<T> : IEventHandler where T : IIntegrationEvent
    {
        public Type EventType => typeof(T);
        public Task<string?> HandleAsync(IIntegrationEvent e, CancellationToken ct) => HandleAsync((T)e, ct);
        protected abstract Task<string?> HandleAsync(T e, CancellationToken ct);
    }
}
