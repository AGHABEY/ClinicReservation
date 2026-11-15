using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Services
{
    /// <summary>Hospital stays: admission, bed moves, discharge, bed-days and the medication round.</summary>
    public class InpatientService
    {
        private readonly AppDbContext _context;
        private readonly StockService _stock;

        public InpatientService(AppDbContext context, StockService stock)
        {
            _context = context;
            _stock = stock;
        }

        public record Result(bool Ok, string? Error, Admission? Admission = null)
        {
            public static Result Fail(string error) => new(false, error);
        }

        /// <summary>Active beds with the stay currently in them (null when free).</summary>
        public List<(Bed Bed, Admission? Stay)> Board(int? departmentId)
        {
            var beds = _context.Beds.Include(b => b.Ward).ThenInclude(w => w.Department)
                .Where(b => b.IsActive && b.Ward.IsActive && (departmentId == null || b.Ward.DepartmentId == departmentId))
                .ToList();
            var bedIds = beds.Select(b => b.Id).ToList();
            var stays = _context.Admissions.Include(a => a.Patient).Include(a => a.Doctor)
                .Where(a => a.Status == AdmissionStatus.Active && a.BedId != null && bedIds.Contains(a.BedId.Value))
                .ToDictionary(a => a.BedId!.Value);
            return beds
                .OrderBy(b => b.Ward.Department?.Name ?? "").ThenBy(b => b.Ward.SortOrder).ThenBy(b => b.Ward.Name)
                .ThenBy(b => b.Name.Length).ThenBy(b => b.Name)
                .Select(b => (b, stays.GetValueOrDefault(b.Id))).ToList();
        }

        public List<Bed> FreeBeds(int? keepBedId = null)
        {
            var taken = _context.Admissions.Where(a => a.Status == AdmissionStatus.Active && a.BedId != null)
                .Select(a => a.BedId!.Value).ToList();
            return _context.Beds.Include(b => b.Ward).ThenInclude(w => w.Department)
                .Where(b => b.IsActive && b.Ward.IsActive && (!taken.Contains(b.Id) || b.Id == keepBedId))
                .ToList()
                .OrderBy(b => b.Ward.Department?.Name ?? "").ThenBy(b => b.Ward.SortOrder).ThenBy(b => b.Ward.Name)
                .ThenBy(b => b.Name.Length).ThenBy(b => b.Name).ToList();
        }

        /// <summary>Warning (not an error) when a man is put into a women's ward or the other way round.</summary>
        public static string? GenderWarning(Bed bed, Patient patient) =>
            (bed.Ward.Gender, patient.Gender) switch
            {
                (WardGender.Male, Gender.Female) => $"{bed.Ward.Name} kişi palatasıdır",
                (WardGender.Female, Gender.Male) => $"{bed.Ward.Name} qadın palatasıdır",
                _ => null
            };

        public Result Admit(int patientId, int bedId, int? doctorId, AdmissionType type, string? icd, string? diagnosis,
            string? note, int? reserveId, string? user)
        {
            var patient = _context.Patients.Find(patientId);
            if (patient == null) return Result.Fail("Xəstə tapılmadı");
            var open = _context.Admissions.FirstOrDefault(a => a.PatientId == patientId && a.Status == AdmissionStatus.Active);
            if (open != null) return Result.Fail($"{patient.FullName} artıq stasionardadır ({open.Number})");
            var bed = _context.Beds.Include(b => b.Ward).FirstOrDefault(b => b.Id == bedId && b.IsActive && b.Ward.IsActive);
            if (bed == null) return Result.Fail("Çarpayını seçin");
            if (_context.Admissions.Any(a => a.BedId == bedId && a.Status == AdmissionStatus.Active))
                return Result.Fail($"{bed.Label} artıq doludur");
            if (doctorId != null && !_context.Doctors.Any(d => d.Id == doctorId)) return Result.Fail("Həkim tapılmadı");

            var now = DateTime.Now;
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
            if (!TrySave(out var err)) return Result.Fail(err!);
            a.Number = $"ST-{a.Id:D5}";
            _context.SaveChanges();
            return new Result(true, null, a);
        }

        public Result Move(Admission a, int bedId, string? user)
        {
            if (!a.IsActive) return Result.Fail("Xəstə artıq çıxarılıb");
            if (a.BedId == bedId) return Result.Fail("Xəstə artıq bu çarpayıdadır");
            var bed = _context.Beds.Include(b => b.Ward).FirstOrDefault(b => b.Id == bedId && b.IsActive && b.Ward.IsActive);
            if (bed == null) return Result.Fail("Çarpayını seçin");
            if (_context.Admissions.Any(x => x.BedId == bedId && x.Status == AdmissionStatus.Active))
                return Result.Fail($"{bed.Label} artıq doludur");

            var now = DateTime.Now;
            var current = _context.BedStays.Where(s => s.AdmissionId == a.Id && s.To == null).ToList();
            foreach (var s in current) s.To = now;
            a.BedId = bedId;
            a.DepartmentId = bed.Ward.DepartmentId ?? a.DepartmentId;
            _context.BedStays.Add(new BedStay { AdmissionId = a.Id, BedId = bedId, From = now, DailyPrice = bed.Ward.DailyPrice });
            return TrySave(out var err) ? new Result(true, null, a) : Result.Fail(err!);
        }

        public Result Discharge(Admission a, DischargeOutcome outcome, string? icd, string diagnosis, string? course,
            string? treatment, string? recommendations, string? user)
        {
            if (!a.IsActive) return Result.Fail("Xəstə artıq çıxarılıb");
            var now = DateTime.Now;
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
            return new Result(true, null, a);
        }

        /// <summary>Removes a stay entered by mistake (only while nothing has been recorded in it).</summary>
        public Result Cancel(Admission a, string reason, string? user)
        {
            if (!a.IsActive) return Result.Fail("Yalnız aktiv yerləşdirmə ləğv edilir");
            if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Ləğv səbəbini yazın");
            if (_context.Observations.Any(o => o.AdmissionId == a.Id) || _context.MedicationOrders.Any(o => o.AdmissionId == a.Id))
                return Result.Fail("Qeyd və ya təyinat yazılıb — ləğv etmək olmaz, xəstəni çıxarış ilə çıxarın");
            var now = DateTime.Now;
            foreach (var s in _context.BedStays.Where(s => s.AdmissionId == a.Id && s.To == null)) s.To = now;
            a.Status = AdmissionStatus.Cancelled;
            a.CancelReason = reason.Trim();
            a.DischargedAt = now;
            a.DischargedBy = user;
            a.BedId = null;
            _context.SaveChanges();
            return new Result(true, null, a);
        }

        public record BedDayLine(BedStay Stay, int Days, decimal Amount);

        /// <summary>
        /// Bed-days per stay: nights spent (a same-day stay counts as one day for the whole admission).
        /// </summary>
        public static (List<BedDayLine> Lines, int Days, decimal Total) BedDays(Admission a, DateTime now)
        {
            var lines = a.Stays.Where(s => s.To == null || s.To > s.From).OrderBy(s => s.From)
                .Select(s => { var d = Math.Max(0, ((s.To ?? now).Date - s.From.Date).Days); return new BedDayLine(s, d, d * s.DailyPrice); })
                .ToList();
            if (lines.Count > 0 && lines.Sum(l => l.Days) == 0)
                lines[0] = lines[0] with { Days = 1, Amount = lines[0].Stay.DailyPrice };
            return (lines, lines.Sum(l => l.Days), lines.Sum(l => l.Amount));
        }

        /// <summary>Records a dose as given or skipped; a given dose of a stock item leaves the store.</summary>
        public Result RecordDose(MedicationOrder order, DateTime slot, DoseStatus status, string? note, string? user, out string? stockWarning)
        {
            stockWarning = null;
            var a = order.Admission;
            if (!a.IsActive) return Result.Fail("Xəstə artıq çıxarılıb");
            var now = DateTime.Now;
            if (order.IsPrn)
            {
                if (status != DoseStatus.Given) return Result.Fail("Lazım olduqda verilən dərman yalnız «verildi» kimi qeyd olunur");
                if (order.StoppedAt != null) return Result.Fail("Təyinat dayandırılıb");
                slot = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            }
            else
            {
                if (!order.SlotsOn(slot.Date).Contains(slot)) return Result.Fail("Bu vaxt üçün təyinat yoxdur");
                if (slot > now.AddHours(2)) return Result.Fail("Dozanı vaxtından 2 saatdan çox əvvəl qeyd etmək olmaz");
            }
            if (status == DoseStatus.Skipped && string.IsNullOrWhiteSpace(note)) return Result.Fail("Niyə verilmədiyini yazın");
            if (_context.MedicationDoses.Any(d => d.MedicationOrderId == order.Id && d.ScheduledAt == slot))
                return Result.Fail("Bu doza artıq qeyd olunub");

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
            if (!TrySave(out var err)) return Result.Fail("Bu doza artıq qeyd olunub");

            if (status == DoseStatus.Given && order.StockItemId != null)
            {
                var o = _stock.IssueToPatient(a.PatientId, a.Id, a.DepartmentId, order.StockItemId.Value, order.QuantityPerDose,
                    $"{a.Number}: {order.Name} {order.Dose}".Trim(), user);
                if (o.Document != null)
                {
                    var saved = _context.MedicationDoses.Find(dose.Id);
                    if (saved != null) { saved.StockDocumentId = o.Document.Id; _context.SaveChanges(); }
                }
                stockWarning = StockService.Warning(o)?.Replace("Materiallar anbardan silinmədi — rezervasiya səhifəsindən yenidən silin.", "Dərman anbardan silinmədi — anbarda silinmə sənədi ilə silin.");
            }
            return new Result(true, null, a);
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
