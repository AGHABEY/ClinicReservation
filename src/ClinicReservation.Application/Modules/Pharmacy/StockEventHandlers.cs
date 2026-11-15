using ClinicReservation.Data;
using ClinicReservation.Events;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Pharmacy
{
    // How the pharmacy reacts to the other modules. Failures throw, so the outbox retries them;
    // every handler is safe to run twice (materials are marked as taken, a dose remembers its document).

    /// <summary>A completed visit takes its services' materials from the department's store.</summary>
    public sealed class TakeMaterialsOnVisitCompleted : Events.EventHandler<VisitCompleted>
    {
        private readonly StockService _stock;
        public TakeMaterialsOnVisitCompleted(StockService stock) => _stock = stock;

        protected override Task<string?> HandleAsync(VisitCompleted e, CancellationToken ct) =>
            Task.FromResult(Checked(_stock.ConsumeForReserve(e.ReserveId, false, e.User)));

        internal static string? Checked(StockService.Outcome o) =>
            o.Errors.Any() ? throw new InvalidOperationException(string.Join("; ", o.Errors)) : StockService.Warning(o);
    }

    /// <summary>Taking a lab sample takes the lab services' tubes, needles and reagents.</summary>
    public sealed class TakeMaterialsOnLabSample : Events.EventHandler<LabSampleCollected>
    {
        private readonly StockService _stock;
        public TakeMaterialsOnLabSample(StockService stock) => _stock = stock;

        protected override Task<string?> HandleAsync(LabSampleCollected e, CancellationToken ct) =>
            Task.FromResult(TakeMaterialsOnVisitCompleted.Checked(_stock.ConsumeForReserve(e.ReserveId, true, e.User)));
    }

    /// <summary>A given dose of a stock medicine leaves the ward's store; the dose keeps the document's id.</summary>
    public sealed class IssueMedicineOnDoseGiven : Events.EventHandler<DoseGiven>
    {
        private readonly IAppDbContext _db;
        private readonly StockService _stock;

        public IssueMedicineOnDoseGiven(IAppDbContext db, StockService stock)
        {
            _db = db;
            _stock = stock;
        }

        protected override async Task<string?> HandleAsync(DoseGiven e, CancellationToken ct)
        {
            var dose = await _db.MedicationDoses.FirstOrDefaultAsync(d => d.MedicationOrderId == e.OrderId && d.ScheduledAt == e.ScheduledAt, ct);
            if (dose == null || dose.StockDocumentId != null) return null; // gone, or already issued
            var o = _stock.IssueToPatient(e.PatientId, e.AdmissionId, e.DepartmentId, e.StockItemId, e.Quantity, e.Note, e.User);
            if (o.Errors.Any()) throw new InvalidOperationException(string.Join("; ", o.Errors));
            if (o.Document != null)
            {
                dose.StockDocumentId = o.Document.Id;
                await _db.SaveChangesAsync(ct);
            }
            return StockService.Warning(o);
        }
    }
}
