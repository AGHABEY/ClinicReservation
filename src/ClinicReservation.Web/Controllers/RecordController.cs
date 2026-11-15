using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Elektron tibbi kart: the doctor's note for a visit.</summary>
    [HasPermission(Permissions.RecordsView, Permissions.RecordsEdit)]
    public class RecordController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public RecordController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        [HasPermission(Permissions.RecordsEdit)]
        public IActionResult Edit(int reserveId)
        {
            var reserve = LoadReserve(reserveId);
            if (reserve == null) return NotFound();
            var denied = CheckCanWrite(reserve);
            if (denied != null) return denied;

            var rec = reserve.MedicalRecord;
            var model = new RecordFormViewModel
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
            Prepare(model, reserve);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.RecordsEdit)]
        public IActionResult Edit(RecordFormViewModel model)
        {
            var reserve = LoadReserve(model.ReserveId);
            if (reserve == null) return NotFound();
            var denied = CheckCanWrite(reserve);
            if (denied != null) return denied;

            var finish = model.Finish == "1";
            if (finish && string.IsNullOrWhiteSpace(model.Diagnosis))
                ModelState.AddModelError(nameof(model.Diagnosis), "Qəbulu bitirmək üçün diaqnozu yazın");

            if (!ModelState.IsValid)
            {
                model.Exists = reserve.MedicalRecord != null;
                Prepare(model, reserve);
                return View(model);
            }

            var now = ClinicTime.Now;
            var userName = User.Identity?.Name;
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
            if (isNew) _context.MedicalRecords.Add(rec);

            var patient = reserve.Patient;
            var alertsChanged = patient.Allergies != Clean(model.Allergies) || patient.ChronicConditions != Clean(model.ChronicConditions);
            patient.Allergies = Clean(model.Allergies);
            patient.ChronicConditions = Clean(model.ChronicConditions);

            // Writing the note means the visit has started.
            var status = reserve.Status?.Name;
            if (status == StatusNames.Arrived)
            {
                reserve.Status = _context.ReserveStatuses.First(s => s.Name == StatusNames.InProgress);
                reserve.StartedAt ??= now;
                status = StatusNames.InProgress;
            }
            if (finish && status == StatusNames.InProgress)
            {
                reserve.Status = _context.ReserveStatuses.First(s => s.Name == StatusNames.Completed);
                reserve.CompletedAt = now;
            }

            _context.SaveChanges();

            _audit.Log(isNew ? "Tibbi qeyd yazıldı" : "Tibbi qeyd dəyişdirildi", AuditEntities.MedicalRecord, rec.Id,
                $"{patient.FullName}: {rec.IcdCode} {rec.Diagnosis}".Trim());
            if (alertsChanged)
                _audit.Log("Allergiya / xroniki xəstəlik dəyişdirildi", AuditEntities.Patient, patient.Id, patient.FullName);
            if (finish && reserve.Status?.Name == StatusNames.Completed)
                _audit.Log("Status dəyişdi", AuditEntities.Reservation, reserve.Id, $"{patient.FullName}: Qəbulda → Tamamlandı");

            TempData["Success"] = finish ? $"{patient.FullName} — qəbul tamamlandı, tibbi qeyd saxlanıldı" : "Tibbi qeyd saxlanıldı";
            if (finish && reserve.Status?.Name == StatusNames.Completed)
            {
                var warn = StockService.Warning(HttpContext.RequestServices.GetRequiredService<StockService>()
                    .ConsumeForReserve(reserve.Id, false, userName));
                if (warn != null) TempData["Error"] = warn;
            }
            return finish
                ? RedirectToAction(nameof(Show), new { id = rec.Id })
                : RedirectToAction(nameof(Edit), new { reserveId = reserve.Id });
        }

        [HasPermission(Permissions.RecordsView)]
        public IActionResult Show(int id)
        {
            var rec = LoadRecord(id);
            if (rec == null) return NotFound();
            _audit.Log("Tibbi qeydə baxıldı", AuditEntities.MedicalRecord, rec.Id, rec.Patient.FullName);
            ViewBag.CanWrite = User.Can(Permissions.RecordsEdit) && CheckCanWrite(rec.Reserve, quiet: true) == null;
            return View(rec);
        }

        [HasPermission(Permissions.RecordsView)]
        public IActionResult Print(int id)
        {
            var rec = LoadRecord(id);
            if (rec == null) return NotFound();
            _audit.Log("Tibbi qeyd çap edildi", AuditEntities.MedicalRecord, rec.Id, rec.Patient.FullName);
            return View(rec);
        }

        /// <summary>A doctor linked to a login may only write notes for their own patients, and only after check-in.</summary>
        private IActionResult? CheckCanWrite(Reserve reserve, bool quiet = false)
        {
            if (reserve.Status?.Name is not (StatusNames.Arrived or StatusNames.InProgress or StatusNames.Completed))
            {
                if (quiet) return Forbid();
                TempData["Error"] = "Tibbi qeyd yalnız qəbula gəlmiş xəstə üçün yazılır";
                return RedirectToAction("Details", "Reservation", new { id = reserve.Id });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var myDoctor = _context.Doctors.Where(d => d.UserId == userId).Select(d => (int?)d.Id).FirstOrDefault();
            if (myDoctor != null && reserve.DoctorId != myDoctor && !User.IsInRole(AppRoles.Admin))
            {
                if (quiet) return Forbid();
                TempData["Error"] = "Bu qəbul başqa həkimə aiddir — qeydi yalnız həmin həkim yaza bilər";
                return RedirectToAction("Details", "Reservation", new { id = reserve.Id });
            }
            return null;
        }

        /// <summary>
        /// The doctor orders lab examinations during the visit: they are added to the
        /// reservation (price locked now, paid at the cashier) and appear in the lab's list.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.RecordsEdit)]
        public IActionResult OrderLab(int reserveId, List<int>? examinationTypeIds)
        {
            var reserve = LoadReserve(reserveId);
            if (reserve == null) return NotFound();
            var denied = CheckCanWrite(reserve);
            if (denied != null) return denied;
            if (reserve.Status?.Name is not (StatusNames.Arrived or StatusNames.InProgress))
            {
                TempData["Error"] = "Analiz yalnız qəbul zamanı təyin edilir";
                return RedirectToAction(nameof(Edit), new { reserveId });
            }

            var ids = examinationTypeIds ?? new();
            var existing = reserve.ReserveExaminationTypes!.Select(x => x.ExaminationTypeId).ToHashSet();
            var toAdd = _context.ExaminationTypes
                .Where(e => ids.Contains(e.Id) && e.LabTests.Any() && !existing.Contains(e.Id))
                .ToList();
            if (toAdd.Count == 0)
            {
                TempData["Error"] = "Yeni analiz seçilməyib";
                return RedirectToAction(nameof(Edit), new { reserveId });
            }

            foreach (var e in toAdd)
                reserve.ReserveExaminationTypes!.Add(new ReserveExaminationType { ExaminationTypeId = e.Id, Price = e.Price });
            _context.SaveChanges();
            _audit.Log("Həkim analiz təyin etdi", AuditEntities.Reservation, reserve.Id,
                $"{reserve.Patient.FullName}: {string.Join(", ", toAdd.Select(e => e.Name))} (+{Fmt.Money(toAdd.Sum(e => e.Price))})");
            TempData["Success"] = $"Analiz təyin edildi: {string.Join(", ", toAdd.Select(e => e.Name))}. Xəstə kassada ödəyib laboratoriyaya keçə bilər.";
            return RedirectToAction(nameof(Edit), new { reserveId });
        }

        private void Prepare(RecordFormViewModel model, Reserve reserve)
        {
            model.Reserve = reserve;
            model.LabOrders = _context.LabOrders
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .Where(o => o.ReserveId == reserve.Id && o.Status != LabOrderStatus.Cancelled)
                .OrderBy(o => o.CollectedAt)
                .ToList();
            var current = reserve.ReserveExaminationTypes!.Select(x => x.ExaminationTypeId).ToList();
            model.LabExams = _context.ExaminationTypes
                .Where(e => e.LabTests.Any() && !current.Contains(e.Id))
                .OrderBy(e => e.Name)
                .ToList();
            model.History = _context.MedicalRecords
                .Include(m => m.Doctor)
                .Where(m => m.PatientId == reserve.PatientId && m.ReserveId != reserve.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(8)
                .ToList();
        }

        private Reserve? LoadReserve(int id) =>
            _context.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.MedicalRecord)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefault(r => r.Id == id);

        private MedicalRecord? LoadRecord(int id) =>
            _context.MedicalRecords
                .Include(m => m.Patient)
                .Include(m => m.Doctor).ThenInclude(d => d!.Department)
                .Include(m => m.Reserve).ThenInclude(r => r.Status)
                .Include(m => m.Reserve).ThenInclude(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefault(m => m.Id == id);

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
