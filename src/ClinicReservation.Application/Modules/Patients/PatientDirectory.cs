using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Patients
{
    /// <summary>Xəstə kartları: search, the card with visit history, FİN lookup, create and edit (audited).</summary>
    public class PatientDirectory
    {
        public const int PageSize = 50;

        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public PatientDirectory(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        public async Task<(List<PatientRow> Rows, int Total)> SearchAsync(string? q, int page, CancellationToken ct = default)
        {
            var query = _db.Patients.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(p =>
                    (p.Name + " " + p.Surname).ToLower().Contains(term) ||
                    (p.Surname + " " + p.Name).ToLower().Contains(term) ||
                    p.PinCode.ToLower().Contains(term) ||
                    p.Phone.ToLower().Contains(term));
            }
            var total = await query.CountAsync(ct);
            var now = ClinicTime.Now;
            var rows = await query
                .OrderBy(p => p.Surname).ThenBy(p => p.Name)
                .Skip((Math.Max(1, page) - 1) * PageSize).Take(PageSize)
                .Select(p => new PatientRow
                {
                    Patient = p,
                    Visits = p.Reserves.Count(r => r.Status!.Name == StatusNames.Completed),
                    LastVisit = p.Reserves.Where(r => r.AppointmentAt < now && r.Status!.Name != StatusNames.Cancelled)
                        .Max(r => (DateTime?)r.AppointmentAt),
                    NextVisit = p.Reserves.Where(r => r.AppointmentAt >= now && ReserveFlow.Active.Contains(r.Status!.Name))
                        .Min(r => (DateTime?)r.AppointmentAt)
                })
                .ToListAsync(ct);
            return (rows, total);
        }

        /// <summary>The card with all visits (opening it is audited).</summary>
        public async Task<Patient?> CardAsync(int id, CancellationToken ct = default)
        {
            var patient = await _db.Patients
                .Include(p => p.Reserves).ThenInclude(r => r.Status)
                .Include(p => p.Reserves).ThenInclude(r => r.Doctor)
                .Include(p => p.Reserves).ThenInclude(r => r.Payments)
                .Include(p => p.Reserves).ThenInclude(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (patient != null) _audit.Log("Xəstə kartına baxıldı", AuditEntities.Patient, patient.Id, patient.FullName);
            return patient;
        }

        /// <summary>FİN lookup for the reservation and admission forms.</summary>
        public async Task<(Patient Patient, int Visits)?> LookupAsync(string? pin, CancellationToken ct = default)
        {
            var normalized = Patient.NormalizePin(pin);
            var p = await _db.Patients.FirstOrDefaultAsync(x => x.PinCode == normalized, ct);
            if (p == null) return null;
            var visits = await _db.Reserves.CountAsync(r => r.PatientId == p.Id && r.Status!.Name == StatusNames.Completed, ct);
            return (p, visits);
        }

        public async Task<PatientFormViewModel?> FormAsync(int id, CancellationToken ct = default)
        {
            var p = await _db.Patients.FindAsync(new object[] { id }, ct);
            return p == null ? null : new PatientFormViewModel
            {
                Id = p.Id, Name = p.Name, Surname = p.Surname, PinCode = p.PinCode, Phone = p.Phone,
                Birthday = p.Birthday, Notes = p.Notes, Gender = p.Gender
            };
        }

        /// <summary>Creates or updates a card; the FİN must be unique. Null = editing a card that doesn't exist.</summary>
        public async Task<Result?> SaveAsync(PatientFormViewModel model, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var patient = model.Id > 0 ? await _db.Patients.FindAsync(new object[] { model.Id }, ct) : null;
            if (model.Id > 0 && patient == null) return null;

            var r = new Result();
            foreach (var (f, m) in formErrors) r.Fail(m, f);
            var pin = Patient.NormalizePin(model.PinCode);
            if (!string.IsNullOrEmpty(pin) && await _db.Patients.AnyAsync(p => p.PinCode == pin && p.Id != model.Id, ct))
                r.Fail("Bu FİN kodla xəstə artıq mövcuddur", nameof(model.PinCode));
            if (model.Birthday.HasValue && model.Birthday.Value.Date > ClinicTime.Today)
                r.Fail("Doğum tarixi gələcəkdə ola bilməz", nameof(model.Birthday));
            if (!r.Ok) return r;

            var isNew = patient == null;
            patient ??= new Patient { CreatedAt = ClinicTime.Now };
            var before = isNew ? null : $"{patient.FullName}, {patient.PinCode}, {patient.Phone}";
            patient.Name = model.Name!.Trim();
            patient.Surname = model.Surname!.Trim();
            patient.PinCode = pin;
            patient.Phone = model.Phone!.Trim();
            patient.Birthday = model.Birthday!.Value.Date;
            patient.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
            patient.Gender = model.Gender;
            if (isNew) _db.Patients.Add(patient);
            await _db.SaveChangesAsync(ct);

            _audit.Log(isNew ? "Xəstə kartı yaradıldı" : "Xəstə məlumatları dəyişdirildi", AuditEntities.Patient, patient.Id,
                isNew ? $"{patient.FullName} ({patient.PinCode})" : $"{before} → {patient.FullName}, {patient.PinCode}, {patient.Phone}");
            return Result.Success(isNew ? $"{patient.FullName} üçün kart yaradıldı" : "Xəstə məlumatları yeniləndi", patient.Id);
        }
    }
}
