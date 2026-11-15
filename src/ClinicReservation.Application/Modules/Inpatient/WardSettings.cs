using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Inpatient
{
    /// <summary>Palatalar və çarpayılar.</summary>
    public class WardSettings
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public WardSettings(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        public async Task<Result> SaveWardAsync(int id, string? name, int? departmentId, WardGender gender, decimal dailyPrice, int sortOrder,
            bool isActive, int bedCount, CancellationToken ct = default)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100) return Result.Failure("Palatanın adını yazın (ən çox 100 simvol)");
            if (dailyPrice < 0 || dailyPrice > 100000) return Result.Failure("Yataq-gün qiyməti düzgün deyil");
            if (!Enum.IsDefined(gender)) return Result.Failure("Palata növünü seçin");
            if (departmentId != null && !await _db.Departments.AnyAsync(d => d.Id == departmentId, ct)) return Result.Failure("Şöbə tapılmadı");
            var isNew = id == 0;
            if (isNew && (bedCount < 1 || bedCount > 30)) return Result.Failure("Çarpayı sayı 1–30 arasında olmalıdır");

            var ward = isNew ? new Ward() : await _db.Wards.FindAsync(new object[] { id }, ct);
            if (ward == null) return Result.Failure("Palata tapılmadı");
            if (!isActive && ward.IsActive && await _db.Admissions.AnyAsync(a => a.Status == AdmissionStatus.Active && a.Bed!.WardId == ward.Id, ct))
                return Result.Failure($"«{ward.Name}» palatasında xəstə var — əvvəlcə köçürün");
            if (await _db.Wards.AnyAsync(w => w.Id != ward.Id && w.Name == name && w.DepartmentId == departmentId, ct))
                return Result.Failure("Bu şöbədə eyni adlı palata var");

            var oldPrice = ward.DailyPrice;
            ward.Name = name;
            ward.DepartmentId = departmentId;
            ward.Gender = gender;
            ward.DailyPrice = dailyPrice;
            ward.SortOrder = sortOrder;
            ward.IsActive = isActive;
            if (isNew)
            {
                for (var i = 1; i <= bedCount; i++) ward.Beds.Add(new Bed { Name = i.ToString() });
                _db.Wards.Add(ward);
            }
            await _db.SaveChangesAsync(ct);
            _audit.Log(isNew ? "Palata əlavə edildi" : "Palata dəyişdirildi", AuditEntities.Inpatient, null,
                $"{ward.Name}, {InpatientLabels.Gender(ward.Gender)}, yataq-gün {Fmt.Money(ward.DailyPrice)}"
                + (isNew ? $", {bedCount} çarpayı" : oldPrice != ward.DailyPrice ? $" (əvvəl {Fmt.Money(oldPrice)})" : ""));
            return Result.Success(isNew ? $"«{ward.Name}» palatası {bedCount} çarpayı ilə əlavə edildi" : $"«{ward.Name}» yeniləndi", ward.Id);
        }

        public async Task<Result?> AddBedAsync(int wardId, string? name, CancellationToken ct = default)
        {
            var ward = await _db.Wards.Include(w => w.Beds).FirstOrDefaultAsync(w => w.Id == wardId, ct);
            if (ward == null) return null;
            name = string.IsNullOrWhiteSpace(name) ? NextName(ward) : name.Trim();
            if (name.Length > 20) return Result.Failure("Çarpayı adı ən çox 20 simvol");
            if (ward.Beds.Any(b => b.Name == name)) return Result.Failure($"«{ward.Name}» palatasında {name} adlı çarpayı var");
            ward.Beds.Add(new Bed { Name = name });
            await _db.SaveChangesAsync(ct);
            _audit.Log("Çarpayı əlavə edildi", AuditEntities.Inpatient, null, $"{ward.Name} · {name}");
            return Result.Success($"{ward.Name}: {name} nömrəli çarpayı əlavə edildi");
        }

        /// <summary>Takes a bed out of use (repair) or back; an occupied bed can't be switched off.</summary>
        public async Task<Result?> ToggleBedAsync(int bedId, CancellationToken ct = default)
        {
            var bed = await _db.Beds.Include(b => b.Ward).FirstOrDefaultAsync(b => b.Id == bedId, ct);
            if (bed == null) return null;
            if (bed.IsActive && await _db.Admissions.AnyAsync(a => a.BedId == bedId && a.Status == AdmissionStatus.Active, ct))
                return Result.Failure($"{bed.Label} doludur — əvvəlcə xəstəni köçürün");
            bed.IsActive = !bed.IsActive;
            await _db.SaveChangesAsync(ct);
            _audit.Log(bed.IsActive ? "Çarpayı istifadəyə qaytarıldı" : "Çarpayı istifadədən çıxarıldı", AuditEntities.Inpatient, null, bed.Label);
            return Result.Success($"{bed.Label}: {(bed.IsActive ? "istifadədədir" : "istifadədən çıxarıldı")}");
        }

        private static string NextName(Ward w)
        {
            var max = w.Beds.Select(b => int.TryParse(b.Name, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return (max + 1).ToString();
        }
    }
}
