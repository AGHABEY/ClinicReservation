using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Pharmacy;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Inpatient
{
    /// <summary>Read side of the inpatient module: bed board, lists, the case page and its forms.</summary>
    public class InpatientQueries
    {
        private readonly IAppDbContext _db;
        private readonly StockQueries _stock;

        public InpatientQueries(IAppDbContext db, StockQueries stock)
        {
            _db = db;
            _stock = stock;
        }

        /// <summary>A patient's stays (latest 20, cancelled left out) for the patient card.</summary>
        public Task<List<Admission>> ForPatientAsync(int patientId, CancellationToken ct = default) =>
            _db.Admissions.Include(a => a.Department).Include(a => a.Bed).ThenInclude(b => b!.Ward)
                .Where(a => a.PatientId == patientId && a.Status != AdmissionStatus.Cancelled)
                .OrderByDescending(a => a.AdmittedAt).Take(20).ToListAsync(ct);

        /// <summary>Active beds with the stay currently in them (null when free).</summary>
        public async Task<List<(Bed Bed, Admission? Stay)>> BoardBedsAsync(int? departmentId, CancellationToken ct = default)
        {
            var beds = await _db.Beds.Include(b => b.Ward).ThenInclude(w => w.Department)
                .Where(b => b.IsActive && b.Ward.IsActive && (departmentId == null || b.Ward.DepartmentId == departmentId))
                .ToListAsync(ct);
            var bedIds = beds.Select(b => b.Id).ToList();
            var stays = await _db.Admissions.Include(a => a.Patient).Include(a => a.Doctor)
                .Where(a => a.Status == AdmissionStatus.Active && a.BedId != null && bedIds.Contains(a.BedId.Value))
                .ToDictionaryAsync(a => a.BedId!.Value, ct);
            return Sort(beds).Select(b => (b, stays.GetValueOrDefault(b.Id))).ToList();
        }

        public async Task<List<Bed>> FreeBedsAsync(CancellationToken ct = default)
        {
            var taken = await _db.Admissions.Where(a => a.Status == AdmissionStatus.Active && a.BedId != null)
                .Select(a => a.BedId!.Value).ToListAsync(ct);
            var beds = await _db.Beds.Include(b => b.Ward).ThenInclude(w => w.Department)
                .Where(b => b.IsActive && b.Ward.IsActive && !taken.Contains(b.Id))
                .ToListAsync(ct);
            return Sort(beds).ToList();
        }

        private static IEnumerable<Bed> Sort(IEnumerable<Bed> beds) => beds
            .OrderBy(b => b.Ward.Department?.Name ?? "").ThenBy(b => b.Ward.SortOrder).ThenBy(b => b.Ward.Name)
            .ThenBy(b => b.Name.Length).ThenBy(b => b.Name);

        public async Task<InpatientBoardViewModel> BoardAsync(int? departmentId, CancellationToken ct = default)
        {
            var today = ClinicTime.Today;
            return new InpatientBoardViewModel
            {
                Beds = await BoardBedsAsync(departmentId, ct),
                Departments = await _db.Wards.Where(w => w.IsActive && w.Department != null).Select(w => w.Department!).Distinct().OrderBy(d => d.Name).ToListAsync(ct),
                DepartmentId = departmentId,
                AdmittedToday = await _db.Admissions.CountAsync(a => a.Status != AdmissionStatus.Cancelled && a.AdmittedAt >= today, ct),
                DischargedToday = await _db.Admissions.CountAsync(a => a.Status == AdmissionStatus.Discharged && a.DischargedAt >= today, ct),
                ActiveTotal = await _db.Admissions.CountAsync(a => a.Status == AdmissionStatus.Active, ct)
            };
        }

        public async Task<InpatientListViewModel> ListAsync(string? status, DateTime? from, DateTime? to, string? q, CancellationToken ct = default)
        {
            var end = (to ?? ClinicTime.Today).Date;
            var start = (from ?? end.AddDays(-30)).Date;
            if (start > end) (start, end) = (end, start);
            q = q?.Trim();
            var model = new InpatientListViewModel { Status = status == "discharged" ? "discharged" : "active", From = start, To = end, Query = q };

            var query = _db.Admissions.Include(a => a.Patient).Include(a => a.Doctor).Include(a => a.Department)
                .Include(a => a.Bed).ThenInclude(b => b!.Ward).AsQueryable();
            query = model.Status == "active"
                ? query.Where(a => a.Status == AdmissionStatus.Active)
                : query.Where(a => a.Status != AdmissionStatus.Active && a.DischargedAt >= start && a.DischargedAt < end.AddDays(1));
            if (!string.IsNullOrEmpty(q))
            {
                var term = q.ToLower();
                query = query.Where(a => (a.Patient.Name + " " + a.Patient.Surname).ToLower().Contains(term)
                    || a.Patient.PinCode.ToLower().Contains(term) || a.Number.ToLower().Contains(term));
            }
            model.Admissions = await (model.Status == "active" ? query.OrderBy(a => a.AdmittedAt) : query.OrderByDescending(a => a.DischargedAt))
                .Take(300).ToListAsync(ct);
            return model;
        }

        /// <summary>The admission form; from a visit it is pre-filled with the visit's patient, doctor and diagnosis.</summary>
        public async Task<AdmitViewModel?> AdmitFormAsync(int? patientId, int? bedId, int? reserveId, CancellationToken ct = default)
        {
            var model = new AdmitViewModel { PatientId = patientId, BedId = bedId, ReserveId = reserveId };
            if (reserveId != null)
            {
                var r = await _db.Reserves.Include(x => x.MedicalRecord).FirstOrDefaultAsync(x => x.Id == reserveId, ct);
                if (r == null) return null;
                model.PatientId = r.PatientId;
                model.DoctorId = r.DoctorId;
                model.IcdCode = r.MedicalRecord?.IcdCode;
                model.Diagnosis = r.MedicalRecord?.Diagnosis;
            }
            return await PrepareAdmitAsync(model, ct);
        }

        public async Task<AdmitViewModel> PrepareAdmitAsync(AdmitViewModel m, CancellationToken ct = default)
        {
            if (m.PatientId != null) m.Patient = await _db.Patients.FindAsync(new object[] { m.PatientId }, ct);
            m.FreeBeds = await FreeBedsAsync(ct);
            m.Doctors = await _db.Doctors.Where(d => d.IsActive || d.Id == m.DoctorId).OrderBy(d => d.FullName).ToListAsync(ct);
            return m;
        }

        /// <summary>The whole case: patient, beds, notes, orders with their doses.</summary>
        public Task<Admission?> AdmissionAsync(int id, CancellationToken ct = default) => _db.Admissions
            .Include(a => a.Patient)
            .Include(a => a.Bed).ThenInclude(b => b!.Ward)
            .Include(a => a.Department)
            .Include(a => a.Doctor)
            .Include(a => a.Stays).ThenInclude(s => s.Bed).ThenInclude(b => b.Ward)
            .Include(a => a.Observations)
            .Include(a => a.Orders).ThenInclude(o => o.StockItem)
            .Include(a => a.Orders).ThenInclude(o => o.Doses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        /// <summary>The case page model; what is loaded depends on what the user may do.</summary>
        public async Task<AdmissionViewModel> CasePageAsync(Admission a, string? tab, DateTime? day, bool canManage, bool canDoctor, CancellationToken ct = default)
        {
            var vm = new AdmissionViewModel
            {
                Admission = a,
                Tab = tab is "orders" or "mar" or "bill" ? tab : "journal",
                Day = (day ?? ClinicTime.Today).Date,
                NewObservation = new ObservationInput { AdmissionId = a.Id, Kind = canDoctor ? ObservationKind.Doctor : ObservationKind.Nurse },
                NewOrder = new OrderInput { AdmissionId = a.Id, Times = "08:00;20:00" },
                PreviousStays = await _db.Admissions.Include(x => x.Department)
                    .Where(x => x.PatientId == a.PatientId && x.Id != a.Id && x.Status == AdmissionStatus.Discharged)
                    .OrderByDescending(x => x.AdmittedAt).Take(10).ToListAsync(ct)
            };
            if (a.IsActive && canManage) vm.FreeBeds = await FreeBedsAsync(ct);
            if (a.IsActive && canDoctor)
            {
                vm.Medicines = await _db.StockItems.Where(i => i.IsActive && i.Kind == StockItemKind.Medicine)
                    .OrderBy(i => i.Name).ThenBy(i => i.Form).ToListAsync(ct);
                var wh = await _db.Warehouses.Where(w => w.IsActive && w.DepartmentId != null && w.DepartmentId == a.DepartmentId)
                    .Select(w => (int?)w.Id).FirstOrDefaultAsync(ct) ?? await _stock.MainWarehouseIdAsync(ct);
                if (wh != null) vm.OnHand = await _stock.OnHandAsync(wh.Value, ct);
            }
            return vm;
        }

        /// <summary>Discharge form pre-filled with the admission diagnosis and the medication sheet.</summary>
        public static DischargeInput DischargeForm(Admission a)
        {
            var meds = a.Orders.OrderBy(o => o.StartAt)
                .Select(o => $"{o.Name}{(o.Dose != null ? " " + o.Dose : "")}{(o.Route != null ? ", " + o.Route.ToLower(Fmt.Az) : "")}"
                    + $" — {(o.IsPrn ? "lazım olduqda" : $"gündə {o.TimeList.Length} dəfə")}, {Fmt.Date(o.StartAt)}–{Fmt.Date(o.StoppedAt ?? ClinicTime.Today)}")
                .ToList();
            return new DischargeInput
            {
                AdmissionId = a.Id,
                IcdCode = a.AdmitIcdCode,
                Diagnosis = a.AdmitDiagnosis,
                Treatment = meds.Any() ? string.Join("\n", meds) : null
            };
        }

        // ---------- Ward settings ----------

        public async Task<(List<Ward> Wards, List<Department> Departments, HashSet<int> Occupied)> WardsPageAsync(CancellationToken ct = default)
        {
            var departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct);
            var occupied = (await _db.Admissions.Where(a => a.Status == AdmissionStatus.Active && a.BedId != null)
                .Select(a => a.BedId!.Value).ToListAsync(ct)).ToHashSet();
            var wards = await _db.Wards.Include(w => w.Department).Include(w => w.Beds)
                .OrderBy(w => w.Department == null ? "" : w.Department.Name).ThenBy(w => w.SortOrder).ThenBy(w => w.Name).ToListAsync(ct);
            return (wards, departments, occupied);
        }
    }
}
