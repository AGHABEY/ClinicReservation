using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Services
{
    /// <summary>
    /// Stock bookkeeping. Every change goes through a document with movements,
    /// so a lot's quantity is always the sum of its movements and nothing is
    /// edited in place. Lots are taken first-expiry-first-out (FEFO).
    /// </summary>
    public class StockService
    {
        private readonly IAppDbContext _context;
        private readonly ILogger<StockService> _logger;
        private readonly AuditService _audit;

        public StockService(IAppDbContext context, ILogger<StockService> logger, AuditService audit)
        {
            _context = context;
            _logger = logger;
            _audit = audit;
        }

        /// <summary>Lots expiring within this many days are shown as "vaxtı bitir".</summary>
        public const int ExpirySoonDays = 90;

        public record ReceiptLine(int ItemId, decimal Quantity, decimal UnitCost, string? BatchNo, DateTime? ExpiryDate);
        public record OutLine(int ItemId, decimal Quantity);
        public record Shortage(string Item, string Unit, decimal Missing, string Warehouse);

        public class Outcome
        {
            public List<StockDocument> Documents { get; } = new();
            public List<string> Errors { get; } = new();
            public List<Shortage> Shortages { get; } = new();
            public bool Ok => Errors.Count == 0;
            public StockDocument? Document => Documents.FirstOrDefault();
        }

        public Warehouse? MainWarehouse() =>
            _context.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsMain).ThenBy(w => w.Id).FirstOrDefault();

        /// <summary>On-hand quantity per item in one warehouse (or all), only lots that aren't expired when <paramref name="usableOnly"/>.</summary>
        public Dictionary<int, decimal> OnHand(int? warehouseId, bool usableOnly = false)
        {
            var today = DateTime.Today;
            var q = _context.StockBatches.Where(b => b.Quantity > 0);
            if (warehouseId != null) q = q.Where(b => b.WarehouseId == warehouseId);
            if (usableOnly) q = q.Where(b => b.ExpiryDate == null || b.ExpiryDate >= today);
            return q.GroupBy(b => b.StockItemId).Select(g => new { g.Key, Qty = g.Sum(b => b.Quantity) })
                .ToDictionary(x => x.Key, x => x.Qty);
        }

        /// <summary>Usable (or, for write-offs of expired stock, only expired) lots in FEFO order.</summary>
        private List<StockBatch> Lots(int itemId, int warehouseId, bool expiredOnly)
        {
            var today = DateTime.Today;
            var q = _context.StockBatches.Where(b => b.StockItemId == itemId && b.WarehouseId == warehouseId && b.Quantity > 0);
            q = expiredOnly
                ? q.Where(b => b.ExpiryDate != null && b.ExpiryDate < today)
                : q.Where(b => b.ExpiryDate == null || b.ExpiryDate >= today);
            return q.OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAt).ThenBy(b => b.Id).ToList();
        }

        /// <summary>Splits a quantity over lots; returns what couldn't be covered.</summary>
        private decimal Allocate(int itemId, int warehouseId, decimal quantity, bool expiredOnly, List<(StockBatch Lot, decimal Qty)> into)
        {
            var left = quantity;
            foreach (var lot in Lots(itemId, warehouseId, expiredOnly))
            {
                if (left <= 0) break;
                var take = Math.Min(lot.Quantity, left);
                into.Add((lot, take));
                left -= take;
            }
            return left;
        }

        private static StockMovement Move(StockDocument doc, StockBatch lot, decimal qty, DateTime at, decimal? counted = null)
        {
            lot.Quantity += qty;
            var m = new StockMovement
            {
                StockDocument = doc,
                StockItemId = lot.StockItemId,
                StockBatch = lot,
                WarehouseId = lot.WarehouseId,
                Quantity = qty,
                UnitCost = lot.UnitCost,
                At = at,
                Counted = counted
            };
            doc.Movements.Add(m);
            return m;
        }

        private StockDocument NewDoc(StockDocType type, DateTime date, string? user) => new()
        {
            Type = type,
            Date = date,
            CreatedAt = DateTime.Now,
            CreatedBy = user
        };

        /// <summary>
        /// Saves the documents and lot changes in one SaveChanges (atomic on its own), then numbers them.
        /// False when someone else changed the same lots meanwhile.
        /// </summary>
        private bool Commit(Outcome outcome)
        {
            try
            {
                foreach (var d in outcome.Documents) _context.StockDocuments.Add(d);
                _context.SaveChanges();
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                outcome.Documents.Clear();
                outcome.Errors.Add("Eyni məhsul bu anda başqa sənəddə dəyişdi. Qalıqlar yeniləndi, yenidən yoxlayıb təkrar göndərin.");
                return false;
            }
            foreach (var d in outcome.Documents) d.Number = $"{StockLabels.Prefix(d.Type)}-{d.Id:D5}";
            _context.SaveChanges();
            return true;
        }

        private Dictionary<int, StockItem> Items(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            return _context.StockItems.Where(i => list.Contains(i.Id)).ToDictionary(i => i.Id);
        }

        // ------------------------------------------------------------------
        // Documents
        // ------------------------------------------------------------------

        public Outcome Receive(int warehouseId, int? supplierId, string? invoiceNo, DateTime date, string? note,
            IReadOnlyList<ReceiptLine> lines, string? user)
        {
            var o = new Outcome();
            var wh = _context.Warehouses.FirstOrDefault(w => w.Id == warehouseId && w.IsActive);
            if (wh == null) o.Errors.Add("Anbarı seçin");
            if (supplierId != null && !_context.Suppliers.Any(s => s.Id == supplierId)) o.Errors.Add("Təchizatçı tapılmadı");
            if (lines.Count == 0) o.Errors.Add("Ən azı bir məhsul əlavə edin");

            var items = Items(lines.Select(l => l.ItemId));
            var n = 0;
            foreach (var l in lines)
            {
                n++;
                if (!items.TryGetValue(l.ItemId, out var item)) { o.Errors.Add($"Sətir {n}: məhsulu seçin"); continue; }
                if (l.Quantity <= 0) o.Errors.Add($"{item.Name}: miqdar 0-dan böyük olmalıdır");
                if (l.UnitCost < 0) o.Errors.Add($"{item.Name}: qiymət mənfi ola bilməz");
                if (l.ExpiryDate == null && item.Kind is StockItemKind.Medicine or StockItemKind.Reagent)
                    o.Errors.Add($"{item.Name}: dərman və reagent üçün son istifadə tarixini yazın");
                else if (l.ExpiryDate != null && l.ExpiryDate.Value.Date < date.Date)
                    o.Errors.Add($"{item.Name}: son istifadə tarixi keçib ({Fmt.Date(l.ExpiryDate)}) — belə məhsulu qəbul etməyin");
            }
            if (!o.Ok) return o;

            // The document carries the invoice date; movements are stamped with the posting time.
            var now = DateTime.Now;
            var doc = NewDoc(StockDocType.Receipt, date, user);
            doc.ToWarehouseId = warehouseId;
            doc.SupplierId = supplierId;
            doc.InvoiceNo = invoiceNo;
            doc.Note = note;
            foreach (var l in lines)
            {
                var lot = new StockBatch
                {
                    StockItemId = l.ItemId,
                    WarehouseId = warehouseId,
                    BatchNo = l.BatchNo,
                    ExpiryDate = l.ExpiryDate?.Date,
                    UnitCost = l.UnitCost,
                    ReceivedAt = now
                };
                _context.StockBatches.Add(lot);
                Move(doc, lot, l.Quantity, now);
            }
            o.Documents.Add(doc);
            Commit(o);
            return o;
        }

        public Outcome Transfer(int fromId, int toId, string? note, IReadOnlyList<OutLine> lines, string? user)
        {
            var o = new Outcome();
            var from = _context.Warehouses.FirstOrDefault(w => w.Id == fromId && w.IsActive);
            var to = _context.Warehouses.FirstOrDefault(w => w.Id == toId && w.IsActive);
            if (from == null) o.Errors.Add("Hansı anbardan köçürüldüyünü seçin");
            if (to == null) o.Errors.Add("Hansı anbara köçürüldüyünü seçin");
            if (from != null && fromId == toId) o.Errors.Add("Eyni anbara köçürmək olmaz");
            if (lines.Count == 0) o.Errors.Add("Ən azı bir məhsul əlavə edin");
            if (!o.Ok) return o;

            var date = DateTime.Now;
            var doc = NewDoc(StockDocType.Transfer, date, user);
            doc.FromWarehouseId = fromId;
            doc.ToWarehouseId = toId;
            doc.Note = note;
            var items = Items(lines.Select(l => l.ItemId));
            var picks = new List<(StockBatch Lot, decimal Qty)>();
            foreach (var l in Merge(lines))
            {
                if (!items.TryGetValue(l.ItemId, out var item)) { o.Errors.Add("Məhsulu seçin"); continue; }
                if (l.Quantity <= 0) { o.Errors.Add($"{item.Name}: miqdar 0-dan böyük olmalıdır"); continue; }
                var missing = Allocate(l.ItemId, fromId, l.Quantity, false, picks);
                if (missing > 0)
                    o.Errors.Add($"{item.Name}: «{from!.Name}» anbarında yararlı qalıq {Qty(l.Quantity - missing)} {item.Unit}, istənilən {Qty(l.Quantity)}");
            }
            if (!o.Ok) return o;

            foreach (var (lot, qty) in picks)
            {
                // Same lot in the destination keeps its batch number, expiry and cost.
                var target = _context.StockBatches.Local.FirstOrDefault(b => Same(b, lot, toId))
                    ?? _context.StockBatches.FirstOrDefault(b => b.StockItemId == lot.StockItemId && b.WarehouseId == toId
                        && b.BatchNo == lot.BatchNo && b.ExpiryDate == lot.ExpiryDate && b.UnitCost == lot.UnitCost);
                if (target == null)
                {
                    target = new StockBatch
                    {
                        StockItemId = lot.StockItemId, WarehouseId = toId, BatchNo = lot.BatchNo,
                        ExpiryDate = lot.ExpiryDate, UnitCost = lot.UnitCost, ReceivedAt = lot.ReceivedAt
                    };
                    _context.StockBatches.Add(target);
                }
                Move(doc, lot, -qty, date);
                Move(doc, target, qty, date);
            }
            o.Documents.Add(doc);
            Commit(o);
            return o;
        }

        private static bool Same(StockBatch b, StockBatch lot, int warehouseId) =>
            b.StockItemId == lot.StockItemId && b.WarehouseId == warehouseId && b.BatchNo == lot.BatchNo
            && b.ExpiryDate == lot.ExpiryDate && b.UnitCost == lot.UnitCost;

        public Outcome WriteOff(int warehouseId, string reason, string? note, int? patientId, int? reserveId,
            IReadOnlyList<OutLine> lines, string? user)
        {
            var o = new Outcome();
            var wh = _context.Warehouses.FirstOrDefault(w => w.Id == warehouseId && w.IsActive);
            if (wh == null) o.Errors.Add("Anbarı seçin");
            if (string.IsNullOrWhiteSpace(reason)) o.Errors.Add("Silinmə səbəbini seçin");
            if (lines.Count == 0) o.Errors.Add("Ən azı bir məhsul əlavə edin");
            if (!o.Ok) return o;

            // Expired stock can only leave as "vaxtı keçib"; everything else uses usable lots only.
            var expiredOnly = reason == "Vaxtı keçib";
            var date = DateTime.Now;
            var doc = NewDoc(StockDocType.WriteOff, date, user);
            doc.FromWarehouseId = warehouseId;
            doc.Reason = reason.Trim();
            doc.Note = note;
            doc.PatientId = patientId;
            doc.ReserveId = reserveId;
            var items = Items(lines.Select(l => l.ItemId));
            var picks = new List<(StockBatch Lot, decimal Qty)>();
            foreach (var l in Merge(lines))
            {
                if (!items.TryGetValue(l.ItemId, out var item)) { o.Errors.Add("Məhsulu seçin"); continue; }
                if (l.Quantity <= 0) { o.Errors.Add($"{item.Name}: miqdar 0-dan böyük olmalıdır"); continue; }
                var missing = Allocate(l.ItemId, warehouseId, l.Quantity, expiredOnly, picks);
                if (missing > 0)
                    o.Errors.Add($"{item.Name}: «{wh!.Name}» anbarında {(expiredOnly ? "vaxtı keçmiş" : "yararlı")} qalıq {Qty(l.Quantity - missing)} {item.Unit}, istənilən {Qty(l.Quantity)}");
            }
            if (!o.Ok) return o;

            foreach (var (lot, qty) in picks) Move(doc, lot, -qty, date);
            o.Documents.Add(doc);
            Commit(o);
            return o;
        }

        /// <summary>Inventory count: posts the difference between counted and system quantity per lot.</summary>
        public Outcome Count(int warehouseId, string? note, IReadOnlyDictionary<int, decimal> counted, string? user)
        {
            var o = new Outcome();
            var wh = _context.Warehouses.FirstOrDefault(w => w.Id == warehouseId && w.IsActive);
            if (wh == null) { o.Errors.Add("Anbarı seçin"); return o; }
            var ids = counted.Keys.ToList();
            var lots = _context.StockBatches.Include(b => b.StockItem)
                .Where(b => ids.Contains(b.Id) && b.WarehouseId == warehouseId).ToList();
            foreach (var lot in lots.Where(l => counted[l.Id] < 0))
                o.Errors.Add($"{lot.StockItem.Name}: sayılan miqdar mənfi ola bilməz");
            if (!o.Ok) return o;

            var date = DateTime.Now;
            var doc = NewDoc(StockDocType.Count, date, user);
            doc.FromWarehouseId = warehouseId;
            doc.ToWarehouseId = warehouseId;
            doc.Note = note;
            foreach (var lot in lots)
            {
                var diff = counted[lot.Id] - lot.Quantity;
                if (diff != 0) Move(doc, lot, diff, date, counted[lot.Id]);
            }
            o.Documents.Add(doc);
            Commit(o);
            return o;
        }

        /// <summary>Undoes a document with reversing movements (only if the stock it brought in is still there).</summary>
        public Outcome Cancel(int documentId, string reason, string? user)
        {
            var o = new Outcome();
            var doc = _context.StockDocuments.Include(d => d.Movements).ThenInclude(m => m.StockBatch).ThenInclude(b => b.StockItem)
                .Include(d => d.Movements).ThenInclude(m => m.Warehouse)
                .FirstOrDefault(d => d.Id == documentId);
            if (doc == null) { o.Errors.Add("Sənəd tapılmadı"); return o; }
            if (doc.Status == StockDocStatus.Cancelled) { o.Errors.Add("Sənəd artıq ləğv edilib"); return o; }
            if (string.IsNullOrWhiteSpace(reason)) { o.Errors.Add("Ləğv səbəbini yazın"); return o; }

            // Check per lot (a transfer can touch the same lot more than once).
            foreach (var g in doc.Lines.GroupBy(m => m.StockBatch))
            {
                var after = g.Key.Quantity - g.Sum(m => m.Quantity);
                if (after < 0)
                    o.Errors.Add($"{g.Key.StockItem.Name}{(g.Key.BatchNo != null ? $", seriya {g.Key.BatchNo}" : "")} ({g.First().Warehouse.Name}): {Qty(-after)} {g.Key.StockItem.Unit} artıq istifadə olunub — əvvəlcə onu qaytarın");
            }
            if (!o.Ok) return o;

            var now = DateTime.Now;
            foreach (var m in doc.Lines.ToList())
            {
                var rev = Move(doc, m.StockBatch, -m.Quantity, now);
                rev.Counted = null;
                rev.IsReversal = true;
            }
            doc.Status = StockDocStatus.Cancelled;
            doc.CancelledAt = now;
            doc.CancelledBy = user;
            doc.CancelReason = reason.Trim();
            try
            {
                _context.SaveChanges();
                o.Documents.Add(doc);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                o.Errors.Add("Qalıq bu anda başqa sənəddə dəyişdi, yenidən cəhd edin");
            }
            return o;
        }

        // ------------------------------------------------------------------
        // Materials of services
        // ------------------------------------------------------------------

        /// <summary>Lines of a visit whose materials have not been taken yet.</summary>
        public List<ReserveExaminationType> PendingConsumption(int reserveId) =>
            _context.ReserveExaminationTypes
                .Include(x => x.ExaminationType).ThenInclude(e => e.Materials).ThenInclude(m => m.StockItem)
                .Include(x => x.ExaminationType).ThenInclude(e => e.LabTests)
                .Where(x => x.ReserveId == reserveId && x.StockConsumedAt == null)
                .ToList();

        /// <summary>
        /// Takes the materials of a visit's services from stock: from the department's own
        /// warehouse when there is one, otherwise from the main one. What isn't in stock is
        /// written in the document's note and reported back — the visit itself is never blocked.
        /// </summary>
        /// <param name="labOnly">Only the lab services (called when the sample is taken).</param>
        public Outcome ConsumeForReserve(int reserveId, bool labOnly, string? user)
        {
            var o = new Outcome();
            try
            {
                var lines = PendingConsumption(reserveId);
                if (labOnly) lines = lines.Where(l => l.ExaminationType.LabTests.Any()).ToList();
                if (lines.Count == 0) return o;

                var reserve = _context.Reserves.First(r => r.Id == reserveId);
                var now = DateTime.Now;
                var warehouses = _context.Warehouses.Where(w => w.IsActive).ToList();
                var main = warehouses.OrderByDescending(w => w.IsMain).ThenBy(w => w.Id).FirstOrDefault();

                var needs = new Dictionary<(int Wh, int Item), decimal>();
                var itemById = new Dictionary<int, StockItem>();
                foreach (var line in lines)
                {
                    line.StockConsumedAt = now;
                    var wh = warehouses.FirstOrDefault(w => w.DepartmentId != null && w.DepartmentId == line.ExaminationType.DepartmentId) ?? main;
                    if (wh == null) continue;
                    foreach (var m in line.ExaminationType.Materials.Where(m => m.Quantity > 0))
                    {
                        needs[(wh.Id, m.StockItemId)] = needs.GetValueOrDefault((wh.Id, m.StockItemId)) + m.Quantity;
                        itemById[m.StockItemId] = m.StockItem;
                    }
                }

                foreach (var group in needs.GroupBy(n => n.Key.Wh))
                {
                    var wh = warehouses.First(w => w.Id == group.Key);
                    var doc = NewDoc(StockDocType.Consumption, now, user);
                    doc.FromWarehouseId = wh.Id;
                    doc.ReserveId = reserveId;
                    doc.PatientId = reserve.PatientId;
                    doc.Reason = "Xidmət sərfiyyatı";
                    var shortNotes = new List<string>();
                    foreach (var need in group)
                    {
                        var picks = new List<(StockBatch Lot, decimal Qty)>();
                        var missing = Allocate(need.Key.Item, wh.Id, need.Value, false, picks);
                        foreach (var (lot, qty) in picks) Move(doc, lot, -qty, now);
                        if (missing > 0)
                        {
                            var item = itemById[need.Key.Item];
                            o.Shortages.Add(new Shortage(item.Name, item.Unit, missing, wh.Name));
                            shortNotes.Add($"{item.Name}: {Qty(missing)} {item.Unit}");
                        }
                    }
                    if (shortNotes.Any()) doc.Note = "Anbarda çatışmadı (silinmədi): " + string.Join("; ", shortNotes);
                    o.Documents.Add(doc);
                }

                if (o.Documents.Count == 0)
                {
                    _context.SaveChanges(); // only the "consumed" marks
                    return o;
                }
                if (Commit(o))
                    foreach (var d in o.Documents)
                        _audit.Log("Xidmət materialları silindi", AuditEntities.Stock, d.Id,
                            $"{d.Number}, rezervasiya #{reserveId}, {d.Movements.Count} sətir" + (d.Note != null ? $"; {d.Note}" : ""), user);
            }
            catch (Exception ex)
            {
                // Stock must never break completing a visit or taking a sample.
                _logger.LogError(ex, "Material sərfiyyatı alınmadı (rezervasiya {ReserveId})", reserveId);
                _context.ChangeTracker.Clear();
                o.Documents.Clear();
                o.Errors.Add("Materiallar anbardan silinmədi");
            }
            return o;
        }

        /// <summary>
        /// A dose given on the ward: takes the medicine from the department's store (or the main one).
        /// Like service materials, a shortage is recorded but never blocks giving the dose.
        /// </summary>
        public Outcome IssueToPatient(int patientId, int admissionId, int? departmentId, int itemId, decimal quantity, string note, string? user)
        {
            var o = new Outcome();
            try
            {
                var warehouses = _context.Warehouses.Where(w => w.IsActive).ToList();
                var wh = warehouses.FirstOrDefault(w => w.DepartmentId != null && w.DepartmentId == departmentId)
                    ?? warehouses.OrderByDescending(w => w.IsMain).ThenBy(w => w.Id).FirstOrDefault();
                var item = _context.StockItems.Find(itemId);
                if (wh == null || item == null || quantity <= 0) return o;

                var now = DateTime.Now;
                var doc = NewDoc(StockDocType.WriteOff, now, user);
                doc.FromWarehouseId = wh.Id;
                doc.Reason = "Xəstəyə verildi";
                doc.PatientId = patientId;
                doc.AdmissionId = admissionId;
                var picks = new List<(StockBatch Lot, decimal Qty)>();
                var missing = Allocate(itemId, wh.Id, quantity, false, picks);
                foreach (var (lot, qty) in picks) Move(doc, lot, -qty, now);
                doc.Note = note + (missing > 0 ? $". Anbarda çatışmadı (silinmədi): {Qty(missing)} {item.Unit}" : "");
                if (missing > 0) o.Shortages.Add(new Shortage(item.Name, item.Unit, missing, wh.Name));
                o.Documents.Add(doc);
                Commit(o);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dərman anbardan silinmədi (yerləşdirmə {AdmissionId})", admissionId);
                _context.ChangeTracker.Clear();
                o.Documents.Clear();
                o.Errors.Add("Dərman anbardan silinmədi");
            }
            return o;
        }

        /// <summary>A short message for the flash after a visit completes, or null when all went fine.</summary>
        public static string? Warning(Outcome o)
        {
            if (o.Errors.Any())
                return "Materiallar anbardan silinmədi — rezervasiya səhifəsindən yenidən silin.";
            if (o.Shortages.Any())
                return "Anbarda çatışmayan material: " + string.Join(", ", o.Shortages.Select(s => $"{s.Item} {Qty(s.Missing)} {s.Unit} ({s.Warehouse})"));
            return null;
        }

        // ------------------------------------------------------------------
        // Reports
        // ------------------------------------------------------------------

        public record MovementRow(StockItem Item, decimal Opening, decimal In, decimal Out, decimal Closing, decimal OutValue);

        /// <summary>Opening, in, out and closing per item for a period (transfers ignored when all warehouses are shown).</summary>
        public List<MovementRow> MovementReport(DateTime from, DateTime toExclusive, int? warehouseId)
        {
            var q = _context.StockMovements.AsQueryable();
            if (warehouseId != null) q = q.Where(m => m.WarehouseId == warehouseId);
            else q = q.Where(m => m.StockDocument.Type != StockDocType.Transfer);

            var opening = q.Where(m => m.At < from).GroupBy(m => m.StockItemId)
                .Select(g => new { g.Key, Qty = g.Sum(m => m.Quantity) }).ToDictionary(x => x.Key, x => x.Qty);
            var period = q.Where(m => m.At >= from && m.At < toExclusive).GroupBy(m => m.StockItemId)
                .Select(g => new
                {
                    g.Key,
                    In = g.Sum(m => m.Quantity > 0 ? m.Quantity : 0),
                    Out = g.Sum(m => m.Quantity < 0 ? -m.Quantity : 0),
                    OutValue = g.Sum(m => m.Quantity < 0 ? -m.Quantity * m.UnitCost : (m.IsReversal ? -m.Quantity * m.UnitCost : 0))
                }).ToDictionary(x => x.Key);

            var ids = opening.Keys.Union(period.Keys).ToList();
            var items = _context.StockItems.Where(i => ids.Contains(i.Id)).ToList();
            return items.Select(i =>
                {
                    var open = opening.GetValueOrDefault(i.Id);
                    var p = period.GetValueOrDefault(i.Id);
                    var inQ = p?.In ?? 0;
                    var outQ = p?.Out ?? 0;
                    return new MovementRow(i, open, inQ, outQ, open + inQ - outQ, Math.Max(0, p?.OutValue ?? 0));
                })
                .Where(r => r.Opening != 0 || r.In != 0 || r.Out != 0)
                .OrderBy(r => r.Item.Name).ToList();
        }

        /// <summary>Same item asked twice in one document → one line.</summary>
        private static IEnumerable<OutLine> Merge(IEnumerable<OutLine> lines) =>
            lines.GroupBy(l => l.ItemId).Select(g => new OutLine(g.Key, g.Sum(l => l.Quantity)));

        /// <summary>Quantity for display: "12", "2,5".</summary>
        public static string Qty(decimal v) => v.ToString("#,0.###", Fmt.Az);
    }
}
