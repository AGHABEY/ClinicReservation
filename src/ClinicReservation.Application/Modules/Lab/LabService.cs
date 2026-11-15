using System.Globalization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Lab
{
    public class LabService
    {
        private readonly IAppDbContext _context;

        public LabService(IAppDbContext context) => _context = context;

        /// <summary>Lab tests the reservation's examinations require that have no sample yet.</summary>
        public List<LabTest> PendingTests(int reserveId)
        {
            var required = _context.ReserveExaminationTypes
                .Where(x => x.ReserveId == reserveId)
                .SelectMany(x => x.ExaminationType.LabTests.Select(l => l.LabTest))
                .Where(t => t.IsActive)
                .Distinct()
                .ToList();
            if (required.Count == 0) return required;

            var done = _context.LabResults
                .Where(r => r.LabOrder.ReserveId == reserveId && r.LabOrder.Status != LabOrderStatus.Cancelled)
                .Select(r => r.LabTestId)
                .ToHashSet();
            return required.Where(t => !done.Contains(t.Id)).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToList();
        }

        /// <summary>Creates one sample with a fresh barcode for the given tests.</summary>
        public LabOrder CreateOrder(Reserve reserve, IEnumerable<LabTest> tests, Gender? gender, string? userName, DateTime now)
        {
            var order = new LabOrder
            {
                ReserveId = reserve.Id,
                PatientId = reserve.PatientId,
                CollectedAt = now,
                CollectedBy = userName,
                Status = LabOrderStatus.Collected,
                Results = tests.Select(t => new LabResult
                {
                    LabTestId = t.Id,
                    Unit = t.Unit,
                    Reference = LabRef.Text(t, gender)
                }).ToList()
            };

            // Barcode = yyMMdd + running number; retry if two samples are taken at the same moment.
            var prefix = now.ToString("yyMMdd", CultureInfo.InvariantCulture);
            for (var attempt = 0; ; attempt++)
            {
                var last = _context.LabOrders.Where(o => o.Barcode.StartsWith(prefix))
                    .OrderByDescending(o => o.Barcode).Select(o => o.Barcode).FirstOrDefault();
                var next = last == null ? 1 : int.Parse(last[prefix.Length..], CultureInfo.InvariantCulture) + 1;
                order.Barcode = prefix + next.ToString("0000", CultureInfo.InvariantCulture);
                if (attempt == 0) _context.LabOrders.Add(order);
                try
                {
                    _context.SaveChanges();
                    return order;
                }
                catch (DbUpdateException) when (attempt < 3)
                {
                    // Barcode taken in the meantime — try the next number.
                }
            }
        }

        /// <summary>Latest validated value of each test for this patient before the given order.</summary>
        public Dictionary<int, LabResult> PreviousResults(int patientId, int beforeOrderId, IEnumerable<int> testIds)
        {
            var ids = testIds.ToList();
            return _context.LabResults
                .Include(r => r.LabOrder)
                .Where(r => r.LabOrder.PatientId == patientId && r.LabOrderId != beforeOrderId
                            && r.LabOrder.Status == LabOrderStatus.Validated && ids.Contains(r.LabTestId) && r.Value != null)
                .OrderByDescending(r => r.LabOrder.CollectedAt)
                .ToList()
                .GroupBy(r => r.LabTestId)
                .ToDictionary(g => g.Key, g => g.First());
        }
    }
}
