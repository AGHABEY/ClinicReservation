using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Emr
{
    public enum RecordAccess
    {
        Allowed,
        /// <summary>The patient hasn't checked in.</summary>
        NotVisited,
        /// <summary>A doctor's login on another doctor's visit.</summary>
        OtherDoctor
    }

    /// <summary>Elektron tibbi kart: the doctor's note for a visit, lab orders from the visit, viewing and printing.</summary>
    public class MedicalRecords
    {
        private readonly IAppDbContext _db;
        private readonly StockService _stock;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public MedicalRecords(IAppDbContext db, StockService stock, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _stock = stock;
            _audit = audit;
            _user = user;
        }

        public Task<Reserve?> VisitAsync(int reserveId, CancellationToken ct = default) =>
            _db.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.MedicalRecord)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == reserveId, ct);

        /// <summary>All of a patient's notes, newest first (patient card).</summary>
        public Task<List<MedicalRecord>> ForPatientAsync(int patientId, CancellationToken ct = default) =>
            _db.MedicalRecords.Include(m => m.Doctor).Where(m => m.PatientId == patientId)
                .OrderByDescending(m => m.CreatedAt).ToListAsync(ct);

        public Task<MedicalRecord?> RecordAsync(int id, CancellationToken ct = default) =>
            _db.MedicalRecords
                .Include(m => m.Patient)
                .Include(m => m.Doctor).ThenInclude(d => d!.Department)
                .Include(m => m.Reserve).ThenInclude(r => r.Status)
                .Include(m => m.Reserve).ThenInclude(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefaultAsync(m => m.Id == id, ct);

        /// <summary>A doctor linked to a login may only write notes for their own patients, and only after check-in.</summary>
        public async Task<RecordAccess> CanWriteAsync(Reserve reserve, bool isAdmin, CancellationToken ct = default)
        {
            if (reserve.Status?.Name is not (StatusNames.Arrived or StatusNames.InProgress or StatusNames.Completed))
                return RecordAccess.NotVisited;
            var myDoctor = _user.UserId == null ? null
                : await _db.Doctors.Where(d => d.UserId == _user.UserId).Select(d => (int?)d.Id).FirstOrDefaultAsync(ct);
            return myDoctor != null && reserve.DoctorId != myDoctor && !isAdmin ? RecordAccess.OtherDoctor : RecordAccess.Allowed;
        }

        public static string Message(RecordAccess a) => a switch
        {
            RecordAccess.NotVisited => "Tibbi qeyd yalnız qəbula gəlmiş xəstə üçün yazılır",
            RecordAccess.OtherDoctor => "Bu qəbul başqa həkimə aiddir — qeydi yalnız həmin həkim yaza bilər",
            _ => ""
        };

        public static RecordFormViewModel Form(Reserve reserve)
        {
            var rec = reserve.MedicalRecord;
            return new RecordFormViewModel
            {
                ReserveId = reserve.Id,
                Complaint = rec?.Complaint,
                Anamnesis = rec?.Anamnesis,
                Examination = rec?.Examination,
                IcdCode = rec?.IcdCode,
                Diagnosis = rec?.Diagnosis,
                Treatment = rec?.Treatment,
                Prescription = rec?.Prescription,
                Recommendations = rec?.Recommendations,
                FollowUpDays = rec?.FollowUpDays,
                Allergies = reserve.Patient.Allergies,
                ChronicConditions = reserve.Patient.ChronicConditions,
                Exists = rec != null
            };
        }

        /// <summary>Lab samples, orderable lab examinations and the patient's earlier notes for the edit page.</summary>
        public async Task<RecordFormViewModel> PrepareAsync(RecordFormViewModel model, Reserve reserve, CancellationToken ct = default)
        {
            model.Reserve = reserve;
            model.LabOrders = await _db.LabOrders
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .Where(o => o.ReserveId == reserve.Id && o.Status != LabOrderStatus.Cancelled)
                .OrderBy(o => o.CollectedAt)
                .ToListAsync(ct);
            var current = reserve.ReserveExaminationTypes!.Select(x => x.ExaminationTypeId).ToList();
            model.LabExams = await _db.ExaminationTypes
                .Where(e => e.LabTests.Any() && !current.Contains(e.Id))
                .OrderBy(e => e.Name)
                .ToListAsync(ct);
            model.History = await _db.MedicalRecords
                .Include(m => m.Doctor)
                .Where(m => m.PatientId == reserve.PatientId && m.ReserveId != reserve.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(8)
                .ToListAsync(ct);
            return model;
        }

        /// <summary>
        /// Saves the note (and the patient's allergies / chronic conditions). Writing the note starts the visit;
        /// "finish" also completes it (diagnosis required) and takes the services' materials from stock.
        /// </summary>
        public async Task<Result> SaveAsync(Reserve reserve, RecordFormViewModel model, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var finish = model.Finish == "1";
            var r = new Result();
            foreach (var (f, m) in formErrors) r.Fail(m, f);
            if (finish && string.IsNullOrWhiteSpace(model.Diagnosis)) r.Fail("Qəbulu bitirmək üçün diaqnozu yazın", nameof(model.Diagnosis));
            if (!r.Ok) return r;

            var now = ClinicTime.Now;
            var userName = _user.UserName;
            var rec = reserve.MedicalRecord;
            var isNew = rec == null;
            rec ??= new MedicalRecord
            {
                ReserveId = reserve.Id,
                PatientId = reserve.PatientId,
                DoctorId = reserve.DoctorId,
                CreatedAt = now,
                CreatedBy = userName
            };
            if (!isNew)
            {
                rec.UpdatedAt = now;
                rec.UpdatedBy = userName;
            }
            rec.Complaint = Clean(model.Complaint);
            rec.Anamnesis = Clean(model.Anamnesis);
            rec.Examination = Clean(model.Examination);
            rec.IcdCode = Clean(model.IcdCode)?.ToUpperInvariant();
            rec.Diagnosis = Clean(model.Diagnosis);
            rec.Treatment = Clean(model.Treatment);
            rec.Prescription = Clean(model.Prescription);
            rec.Recommendations = Clean(model.Recommendations);
            rec.FollowUpDays = model.FollowUpDays;
            if (isNew) _db.MedicalRecords.Add(rec);

            var patient = reserve.Patient;
            var alertsChanged = patient.Allergies != Clean(model.Allergies) || patient.ChronicConditions != Clean(model.ChronicConditions);
            patient.Allergies = Clean(model.Allergies);
            patient.ChronicConditions = Clean(model.ChronicConditions);

            // Writing the note means the visit has started.
            var status = reserve.Status?.Name;
            if (status == StatusNames.Arrived)
            {
                reserve.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.InProgress, ct);
                reserve.StartedAt ??= now;
                status = StatusNames.InProgress;
            }
            var completed = false;
            if (finish && status == StatusNames.InProgress)
            {
                reserve.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Completed, ct);
                reserve.CompletedAt = now;
                completed = true;
            }
            await _db.SaveChangesAsync(ct);

            _audit.Log(isNew ? "Tibbi qeyd yazıldı" : "Tibbi qeyd dəyişdirildi", AuditEntities.MedicalRecord, rec.Id,
                $"{patient.FullName}: {rec.IcdCode} {rec.Diagnosis}".Trim());
            if (alertsChanged)
                _audit.Log("Allergiya / xroniki xəstəlik dəyişdirildi", AuditEntities.Patient, patient.Id, patient.FullName);
            if (completed)
                _audit.Log("Status dəyişdi", AuditEntities.Reservation, reserve.Id, $"{patient.FullName}: Qəbulda → Tamamlandı");

            var result = Result.Success(finish ? $"{patient.FullName} — qəbul tamamlandı, tibbi qeyd saxlanıldı" : "Tibbi qeyd saxlanıldı", rec.Id);
            if (completed) result.Warning = StockService.Warning(_stock.ConsumeForReserve(reserve.Id, false, userName));
            return result;
        }

        /// <summary>The doctor orders lab examinations during the visit: added to the reservation at today's price.</summary>
        public async Task<Result> OrderLabAsync(Reserve reserve, List<int>? examinationTypeIds, CancellationToken ct = default)
        {
            if (reserve.Status?.Name is not (StatusNames.Arrived or StatusNames.InProgress))
                return Result.Failure("Analiz yalnız qəbul zamanı təyin edilir");
            var ids = examinationTypeIds ?? new();
            var existing = reserve.ReserveExaminationTypes!.Select(x => x.ExaminationTypeId).ToHashSet();
            var toAdd = await _db.ExaminationTypes
                .Where(e => ids.Contains(e.Id) && e.LabTests.Any() && !existing.Contains(e.Id))
                .ToListAsync(ct);
            if (toAdd.Count == 0) return Result.Failure("Yeni analiz seçilməyib");

            foreach (var e in toAdd)
                reserve.ReserveExaminationTypes!.Add(new ReserveExaminationType { ExaminationTypeId = e.Id, Price = e.Price });
            await _db.SaveChangesAsync(ct);
            _audit.Log("Həkim analiz təyin etdi", AuditEntities.Reservation, reserve.Id,
                $"{reserve.Patient.FullName}: {string.Join(", ", toAdd.Select(e => e.Name))} (+{Fmt.Money(toAdd.Sum(e => e.Price))})");
            return Result.Success($"Analiz təyin edildi: {string.Join(", ", toAdd.Select(e => e.Name))}. Xəstə kassada ödəyib laboratoriyaya keçə bilər.");
        }

        public void Viewed(MedicalRecord rec) => _audit.Log("Tibbi qeydə baxıldı", AuditEntities.MedicalRecord, rec.Id, rec.Patient.FullName);
        public void Printed(MedicalRecord rec) => _audit.Log("Tibbi qeyd çap edildi", AuditEntities.MedicalRecord, rec.Id, rec.Patient.FullName);

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
