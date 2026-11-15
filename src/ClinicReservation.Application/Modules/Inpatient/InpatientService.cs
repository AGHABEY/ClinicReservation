using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Events;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Inpatient
{
    /// <summary>Hospital stays: admission, bed moves, discharge, bed-days and the medication round.</summary>
    public class InpatientService
    {
        private readonly IAppDbContext _context;
        private readonly Outbox _outbox;

        public InpatientService(IAppDbContext context, Outbox outbox)
        {
            _context = context;
            _outbox = outbox;
        }

        public record StayResult(bool Ok, string? Error, Admission? Admission = null)
        {
            public static StayResult Fail(string error) => new(false, error);
        }

        public StayResult Admit(int patientId, int bedId, int? doctorId, AdmissionType type, string? icd, string? diagnosis,
            string? note, int? reserveId, string? user)
        {
            var patient = _context.Patients.Find(patientId);
            if (patient == null) return StayResult.Fail("Xəstə tapılmadı");
            var open = _context.Admissions.FirstOrDefault(a => a.PatientId == patientId && a.Status == AdmissionStatus.Active);
            if (open != null) return StayResult.Fail($"{patient.FullName} artıq stasionardadır ({open.Number})");
            var bed = _context.Beds.Include(b => b.Ward).FirstOrDefault(b => b.Id == bedId && b.IsActive && b.Ward.IsActive);
            if (bed == null) return StayResult.Fail("Çarpayını seçin");
            if (_context.Admissions.Any(a => a.BedId == bedId && a.Status == AdmissionStatus.Active))
                return StayResult.Fail($"{bed.Label} artıq doludur");
            if (doctorId != null && !_context.Doctors.Any(d => d.Id == doctorId)) return StayResult.Fail("Həkim tapılmadı");

            var now = ClinicTime.Now;
            var a = new Admission
            {
                PatientId = patientId,
                BedId = bedId,
                DepartmentId = bed.Ward.DepartmentId,
                DoctorId = doctorId,
                ReserveId = reserveId,
                Type = type,
                AdmittedAt = now,
                AdmittedBy = user,
                AdmitIcdCode = icd,
                AdmitDiagnosis = diagnosis,
                AdmitNote = note
            };
            a.Stays.Add(new BedStay { BedId = bedId, From = now, DailyPrice = bed.Ward.DailyPrice });
            _context.Admissions.Add(a);
            if (!TrySave(out var err)) return StayResult.Fail(err!);
            a.Number = $"ST-{a.Id:D5}";
            _context.SaveChanges();
            return new StayResult(true, null, a);
        }

        public StayResult Move(Admission a, int bedId, string? user)
        {
            if (!a.IsActive) return StayResult.Fail("Xəstə artıq çıxarılıb");
            if (a.BedId == bedId) return StayResult.Fail("Xəstə artıq bu çarpayıdadır");
            var bed = _context.Beds.Include(b => b.Ward).FirstOrDefault(b => b.Id == bedId && b.IsActive && b.Ward.IsActive);
            if (bed == null) return StayResult.Fail("Çarpayını seçin");
            if (_context.Admissions.Any(x => x.BedId == bedId && x.Status == AdmissionStatus.Active))
                return StayResult.Fail($"{bed.Label} artıq doludur");

            var now = ClinicTime.Now;
            var current = _context.BedStays.Where(s => s.AdmissionId == a.Id && s.To == null).ToList();
            foreach (var s in current) s.To = now;
            a.BedId = bedId;
            a.DepartmentId = bed.Ward.DepartmentId ?? a.DepartmentId;
            _context.BedStays.Add(new BedStay { AdmissionId = a.Id, BedId = bedId, From = now, DailyPrice = bed.Ward.DailyPrice });
            return TrySave(out var err) ? new StayResult(true, null, a) : StayResult.Fail(err!);
        }

        public StayResult Discharge(Admission a, DischargeOutcome outcome, string? icd, string diagnosis, string? course,
            string? treatment, string? recommendations, string? user)
        {
            if (!a.IsActive) return StayResult.Fail("Xəstə artıq çıxarılıb");
            var now = ClinicTime.Now;
            foreach (var s in _context.BedStays.Where(s => s.AdmissionId == a.Id && s.To == null)) s.To = now;
            // Running orders end with the stay.
            foreach (var o in _context.MedicationOrders.Where(o => o.AdmissionId == a.Id && o.StoppedAt == null))
            {
                o.StoppedAt = now;
                o.StoppedBy = user;
            }
            a.Status = AdmissionStatus.Discharged;
            a.DischargedAt = now;
            a.DischargedBy = user;
            a.Outcome = outcome;
            a.FinalIcdCode = icd;
            a.FinalDiagnosis = diagnosis;
            a.Course = course;
            a.TreatmentSummary = treatment;
            a.Recommendations = recommendations;
            a.BedId = null;
            _context.SaveChanges();
            return new StayResult(true, null, a);
        }

        /// <summary>Removes a stay entered by mistake (only while nothing has been recorded in it).</summary>
        public StayResult Cancel(Admission a, string reason, string? user)
        {
            if (!a.IsActive) return StayResult.Fail("Yalnız aktiv yerləşdirmə ləğv edilir");
            if (string.IsNullOrWhiteSpace(reason)) return StayResult.Fail("Ləğv səbəbini yazın");
            if (_context.Observations.Any(o => o.AdmissionId == a.Id) || _context.MedicationOrders.Any(o => o.AdmissionId == a.Id))
                return StayResult.Fail("Qeyd və ya təyinat yazılıb — ləğv etmək olmaz, xəstəni çıxarış ilə çıxarın");
            var now = ClinicTime.Now;
            foreach (var s in _context.BedStays.Where(s => s.AdmissionId == a.Id && s.To == null)) s.To = now;
            a.Status = AdmissionStatus.Cancelled;
            a.CancelReason = reason.Trim();
            a.DischargedAt = now;
            a.DischargedBy = user;
            a.BedId = null;
            _context.SaveChanges();
            return new StayResult(true, null, a);
        }

        /// <summary>Records a dose as given or skipped; a given dose of a stock item leaves the store.</summary>
        public StayResult RecordDose(MedicationOrder order, DateTime slot, DoseStatus status, string? note, string? user)
        {
            var a = order.Admission;
            if (!a.IsActive) return StayResult.Fail("Xəstə artıq çıxarılıb");
            var now = ClinicTime.Now;
            if (order.IsPrn)
            {
                if (status != DoseStatus.Given) return StayResult.Fail("Lazım olduqda verilən dərman yalnız «verildi» kimi qeyd olunur");
                if (order.StoppedAt != null) return StayResult.Fail("Təyinat dayandırılıb");
                slot = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            }
            else
            {
                if (!order.SlotsOn(slot.Date).Contains(slot)) return StayResult.Fail("Bu vaxt üçün təyinat yoxdur");
                if (slot > now.AddHours(2)) return StayResult.Fail("Dozanı vaxtından 2 saatdan çox əvvəl qeyd etmək olmaz");
            }
            if (status == DoseStatus.Skipped && string.IsNullOrWhiteSpace(note)) return StayResult.Fail("Niyə verilmədiyini yazın");
            if (_context.MedicationDoses.Any(d => d.MedicationOrderId == order.Id && d.ScheduledAt == slot))
                return StayResult.Fail("Bu doza artıq qeyd olunub");

            var dose = new MedicationDose
            {
                MedicationOrderId = order.Id,
                ScheduledAt = slot,
                Status = status,
                RecordedAt = now,
                RecordedBy = user,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            };
            _context.MedicationDoses.Add(dose);
            // A given dose of a stock medicine leaves the ward's store: stored with the dose, handled right after.
            if (status == DoseStatus.Given && order.StockItemId != null)
                _outbox.Add(new DoseGiven(order.Id, slot, a.Id, a.PatientId, a.DepartmentId, order.StockItemId.Value, order.QuantityPerDose,
                    $"{a.Number}: {order.Name} {order.Dose}".Trim(), user));
            if (!TrySave(out _)) return StayResult.Fail("Bu doza artıq qeyd olunub");
            return new StayResult(true, null, a);
        }

        private bool TrySave(out string? error)
        {
            try
            {
                _context.SaveChanges();
                error = null;
                return true;
            }
            catch (DbUpdateException)
            {
                // Unique indexes: the bed or the patient was taken by someone else just now.
                _context.ChangeTracker.Clear();
                error = "Çarpayı və ya xəstə bu anda başqa yerləşdirmədə istifadə olundu — səhifəni yeniləyib yenidən cəhd edin";
                return false;
            }
        }
    }
}
