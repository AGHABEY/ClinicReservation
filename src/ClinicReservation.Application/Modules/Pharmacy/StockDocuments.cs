using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Pharmacy
{
    /// <summary>
    /// Use cases behind the stock document forms: checks what the user typed, posts the document
    /// through StockService and writes the audit entry. Web and a future API call the same methods.
    /// </summary>
    public class StockDocuments
    {
        private readonly IAppDbContext _db;
        private readonly StockService _stock;
        private readonly StockQueries _queries;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public StockDocuments(IAppDbContext db, StockService stock, StockQueries queries, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _stock = stock;
            _queries = queries;
            _audit = audit;
            _user = user;
        }

        public async Task<Result> ReceiveAsync(StockDocFormViewModel form, CancellationToken ct = default)
        {
            var lines = Filled(form);
            var r = new Result();
            if (lines.Any(l => l.ItemId == null || l.Quantity == null)) r.Fail("Hər sətirdə məhsulu və miqdarı yazın");
            if (form.Date.Date > ClinicTime.Today) r.Fail("Qaimə tarixi gələcəkdə ola bilməz");
            if (form.WarehouseId == null) r.Fail("Anbarı seçin");
            if (!r.Ok) return r;

            var o = _stock.Receive(form.WarehouseId!.Value, form.SupplierId, Clean(form.InvoiceNo), form.Date.Date, Clean(form.Note),
                lines.Select(l => new StockService.ReceiptLine(l.ItemId!.Value, l.Quantity!.Value, l.UnitCost ?? 0, Clean(l.BatchNo), l.ExpiryDate)).ToList(),
                _user.UserName);
            return await PostedAsync(o, ct);
        }

        public async Task<Result> TransferAsync(StockDocFormViewModel form, CancellationToken ct = default)
        {
            var lines = Filled(form);
            var r = new Result();
            if (lines.Any(l => l.ItemId == null || l.Quantity == null)) r.Fail("Hər sətirdə məhsulu və miqdarı yazın");
            if (form.WarehouseId == null || form.ToWarehouseId == null) r.Fail("Hər iki anbarı seçin");
            if (!r.Ok) return r;

            var o = _stock.Transfer(form.WarehouseId!.Value, form.ToWarehouseId!.Value, Clean(form.Note),
                lines.Select(l => new StockService.OutLine(l.ItemId!.Value, l.Quantity!.Value)).ToList(), _user.UserName);
            return await PostedAsync(o, ct);
        }

        public async Task<Result> WriteOffAsync(StockDocFormViewModel form, CancellationToken ct = default)
        {
            var lines = Filled(form);
            var r = new Result();
            if (lines.Any(l => l.ItemId == null || l.Quantity == null)) r.Fail("Hər sətirdə məhsulu və miqdarı yazın");
            if (form.WarehouseId == null) r.Fail("Anbarı seçin");
            if (string.IsNullOrWhiteSpace(form.Reason) || !StockLabels.WriteOffReasons.Contains(form.Reason)) r.Fail("Silinmə səbəbini seçin");

            int? patientId = null;
            if (form.ReserveId != null)
            {
                patientId = await _db.Reserves.Where(x => x.Id == form.ReserveId).Select(x => (int?)x.PatientId).FirstOrDefaultAsync(ct);
                if (patientId == null) r.Fail("Rezervasiya tapılmadı");
            }
            else if (!string.IsNullOrWhiteSpace(form.PatientPin))
            {
                var pin = Patient.NormalizePin(form.PatientPin);
                patientId = await _db.Patients.Where(p => p.PinCode == pin).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
                if (patientId == null) r.Fail($"{pin} FİN-li xəstə tapılmadı");
            }
            if (form.Reason == "Xəstəyə verildi" && patientId == null) r.Fail("Xəstəyə verilən dərman üçün xəstənin FİN-ini yazın");
            if (!r.Ok) return r;

            var o = _stock.WriteOff(form.WarehouseId!.Value, form.Reason!, Clean(form.Note), patientId, form.ReserveId,
                lines.Select(l => new StockService.OutLine(l.ItemId!.Value, l.Quantity!.Value)).ToList(), _user.UserName);
            return await PostedAsync(o, ct);
        }

        public Task<Result> CountAsync(int warehouseId, string? note, IDictionary<int, decimal?> counted, CancellationToken ct = default)
        {
            // Only lines someone actually filled in count; empty means "not counted".
            var filled = counted.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!.Value);
            if (filled.Count == 0)
                return Task.FromResult(Result.Failure("Heç bir sətir doldurulmayıb — sayılan miqdarları yazın"));

            var o = _stock.Count(warehouseId, Clean(note), filled, _user.UserName);
            if (!o.Ok) return Task.FromResult(Errors(o));
            var doc = o.Document!;
            _audit.Log("İnventarizasiya", AuditEntities.Stock, doc.Id, $"{doc.Number}: {filled.Count} sətir sayıldı, {doc.Movements.Count} fərq");
            return Task.FromResult(Result.Success(doc.Movements.Count == 0
                ? $"{doc.Number}: sayılan miqdarlar sistemlə üst-üstə düşür"
                : $"{doc.Number}: {doc.Movements.Count} sətirdə fərq qeydə alındı", doc.Id));
        }

        public Result Cancel(int documentId, string? reason)
        {
            var o = _stock.Cancel(documentId, reason ?? "", _user.UserName);
            if (!o.Ok) return Errors(o);
            var doc = o.Document!;
            _audit.Log("Anbar sənədi ləğv edildi", AuditEntities.Stock, doc.Id, $"{doc.Number} ({StockLabels.Doc(doc.Type)}): {doc.CancelReason}");
            return Result.Success($"{doc.Number} ləğv edildi, qalıqlar geri qaytarıldı", doc.Id);
        }

        /// <summary>Takes a completed visit's materials again (after a failure or a shortage was fixed).</summary>
        public async Task<Result> ConsumeForReserveAsync(int reserveId, CancellationToken ct = default)
        {
            var status = await _db.Reserves.Where(x => x.Id == reserveId).Select(x => x.Status!.Name).FirstOrDefaultAsync(ct);
            if (status != StatusNames.Completed) return Result.Failure("Materiallar qəbul tamamlananda silinir");
            var o = _stock.ConsumeForReserve(reserveId, false, _user.UserName);
            var warn = StockService.Warning(o);
            if (warn != null) return Result.Failure(warn);
            return Result.Success(o.Documents.Any() ? "Materiallar anbardan silindi" : "Silinəcək material yoxdur");
        }

        // ------------------------------------------------------------------

        private async Task<Result> PostedAsync(StockService.Outcome o, CancellationToken ct)
        {
            if (!o.Ok) return Errors(o);
            var doc = (await _queries.DocumentAsync(o.Document!.Id, ct))!;
            var lines = doc.Lines.Where(m => doc.Type != StockDocType.Transfer || m.Quantity > 0).ToList();
            _audit.Log(StockLabels.Doc(doc.Type), AuditEntities.Stock, doc.Id,
                $"{doc.Number}: " + string.Join(", ", lines.GroupBy(m => m.StockItem.Name).Select(g => $"{g.Key} {StockService.Qty(Math.Abs(g.Sum(m => m.Quantity)))}"))
                + (doc.Patient != null ? $"; xəstə {doc.Patient.FullName}" : ""));
            return Result.Success($"{doc.Number} — {StockLabels.Doc(doc.Type).ToLower(Fmt.Az)} qeydə alındı", doc.Id);
        }

        private static Result Errors(StockService.Outcome o)
        {
            var r = new Result();
            foreach (var e in o.Errors) r.Fail(e);
            return r;
        }

        /// <summary>Drops the empty rows the form always posts.</summary>
        private static List<StockLineInput> Filled(StockDocFormViewModel form)
        {
            form.Lines = form.Lines.Where(l => l.ItemId != null || l.Quantity != null || !string.IsNullOrWhiteSpace(l.BatchNo)).ToList();
            return form.Lines;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
