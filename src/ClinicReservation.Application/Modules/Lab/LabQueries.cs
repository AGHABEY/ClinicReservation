using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Lab
{
    /// <summary>Read side of the lab: worklist, orders, labels, the test catalog.</summary>
    public class LabQueries
    {
        private readonly IAppDbContext _db;
        private readonly LabService _lab;

        public LabQueries(IAppDbContext db, LabService lab)
        {
            _db = db;
            _lab = lab;
        }

        /// <summary>A scanned 10-digit barcode → the order it belongs to.</summary>
        public Task<int?> OrderIdByBarcodeAsync(string barcode, CancellationToken ct = default) =>
            _db.LabOrders.Where(o => o.Barcode == barcode).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);

        // tab: pending (sample not taken yet) | work (results being entered) | done (validated)
        public async Task<LabWorklistViewModel> WorklistAsync(string? tab, DateTime day, string? q, CancellationToken ct = default)
        {
            var model = new LabWorklistViewModel { Tab = tab is "work" or "done" ? tab : "pending", Date = day, Query = q };
            var pending = await PendingForAsync(day, q, ct);
            var work = OrdersQuery(q).Where(o => o.Status == LabOrderStatus.Collected);
            var done = OrdersQuery(q).Where(o => o.Status == LabOrderStatus.Validated && o.ValidatedAt >= day && o.ValidatedAt < day.AddDays(1));

            model.PendingCount = pending.Count;
            model.WorkCount = await work.CountAsync(ct);
            model.DoneCount = await done.CountAsync(ct);
            model.Pending = pending;
            model.Orders = model.Tab switch
            {
                "work" => await work.OrderBy(o => o.CollectedAt).ToListAsync(ct),
                "done" => await done.OrderByDescending(o => o.ValidatedAt).ToListAsync(ct),
                _ => new List<LabOrder>()
            };
            return model;
        }

        public Task<LabOrder?> OrderAsync(int id, CancellationToken ct = default) =>
            _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Reserve).ThenInclude(r => r.Doctor)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id, ct);

        public LabOrderViewModel OrderPage(LabOrder order, bool canEdit) => new()
        {
            Order = order,
            Previous = _lab.PreviousResults(order.PatientId, order.Id, order.Results.Select(r => r.LabTestId)),
            CanEdit = order.Status == LabOrderStatus.Collected && canEdit
        };

        /// <summary>Orders for barcode stickers. ids = comma separated order ids.</summary>
        public Task<List<LabOrder>> LabelsAsync(string? ids, CancellationToken ct = default)
        {
            var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToList();
            return _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .Where(o => list.Contains(o.Id))
                .ToListAsync(ct);
        }

        public Task<List<LabTest>> CatalogAsync(CancellationToken ct = default) =>
            _db.LabTests
                .Include(t => t.ExaminationTypes).ThenInclude(x => x.ExaminationType)
                .OrderBy(t => t.Category).ThenBy(t => t.SortOrder).ThenBy(t => t.Name)
                .ToListAsync(ct);

        public async Task<LabTest?> TestAsync(int id, CancellationToken ct = default) =>
            await _db.LabTests.FindAsync(new object[] { id }, ct);

        public async Task<int> NextSortOrderAsync(CancellationToken ct = default) =>
            (await _db.LabTests.MaxAsync(t => (int?)t.SortOrder, ct) ?? 0) + 10;

        private IQueryable<LabOrder> OrdersQuery(string? q)
        {
            var query = _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Reserve).ThenInclude(r => r.Doctor)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .AsSplitQuery();
            if (!string.IsNullOrEmpty(q))
            {
                var term = q.ToLower();
                query = query.Where(o => (o.Patient.Name + " " + o.Patient.Surname).ToLower().Contains(term)
                                         || o.Patient.PinCode.ToLower().Contains(term) || o.Barcode.Contains(q));
            }
            return query;
        }

        /// <summary>Reservations on the day whose lab tests have no sample yet.</summary>
        private async Task<List<LabPendingRow>> PendingForAsync(DateTime day, string? q, CancellationToken ct)
        {
            var query = _db.Reserves
                .Include(r => r.Patient)
                .Include(r => r.Status)
                .Include(r => r.Doctor)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType).ThenInclude(e => e.LabTests).ThenInclude(l => l.LabTest)
                .Where(r => r.AppointmentAt >= day && r.AppointmentAt < day.AddDays(1))
                .Where(r => r.Status!.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow)
                .Where(r => r.ReserveExaminationTypes!.Any(x => x.ExaminationType.LabTests.Any()));
            if (!string.IsNullOrEmpty(q))
            {
                var term = q.ToLower();
                query = query.Where(r => (r.Patient.Name + " " + r.Patient.Surname).ToLower().Contains(term) || r.Patient.PinCode.ToLower().Contains(term));
            }
            var reserves = await query.OrderBy(r => r.AppointmentAt).AsSplitQuery().ToListAsync(ct);
            if (reserves.Count == 0) return new();

            var ids = reserves.Select(r => r.Id).ToList();
            var taken = (await _db.LabResults
                .Where(r => ids.Contains(r.LabOrder.ReserveId) && r.LabOrder.Status != LabOrderStatus.Cancelled)
                .Select(r => new { r.LabOrder.ReserveId, r.LabTestId })
                .ToListAsync(ct))
                .ToLookup(x => x.ReserveId, x => x.LabTestId);

            return reserves
                .Select(r => new LabPendingRow
                {
                    Reserve = r,
                    Tests = r.ReserveExaminationTypes!
                        .SelectMany(x => x.ExaminationType.LabTests.Select(l => l.LabTest))
                        .Where(t => t.IsActive && !taken[r.Id].Contains(t.Id))
                        .DistinctBy(t => t.Id)
                        .OrderBy(t => t.SortOrder)
                        .ToList()
                })
                .Where(x => x.Tests.Count > 0)
                .ToList();
        }
    }
}
