using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Lab
{
    /// <summary>Analiz kataloqu: parameters, units and reference ranges.</summary>
    public class LabCatalogSettings
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public LabCatalogSettings(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        /// <summary>Creates or updates a test. Field errors use LabTest property names. Normalises Code/Name on the input.</summary>
        public async Task<Result> SaveAsync(LabTest model, CancellationToken ct = default)
        {
            model.Code = (model.Code ?? "").Trim().ToUpperInvariant();
            model.Name = (model.Name ?? "").Trim();
            var r = new Result();
            if (string.IsNullOrEmpty(model.Code)) r.Fail("Kod daxil edin", nameof(LabTest.Code));
            else if (model.Code.Length > 20) r.Fail("Kod ən çox 20 simvol", nameof(LabTest.Code));
            else if (await _db.LabTests.AnyAsync(t => t.Code == model.Code && t.Id != model.Id, ct))
                r.Fail("Bu kodla analiz artıq var", nameof(LabTest.Code));
            if (string.IsNullOrEmpty(model.Name)) r.Fail("Ad daxil edin", nameof(LabTest.Name));
            if (model.RefMin > model.RefMax) r.Fail("Yuxarı hədd aşağı həddən kiçikdir", nameof(LabTest.RefMax));
            if (model.RefMinFemale > model.RefMaxFemale) r.Fail("Yuxarı hədd aşağı həddən kiçikdir", nameof(LabTest.RefMaxFemale));
            if (model.ResultType == LabResultType.Choice && model.ChoiceList.Length < 2)
                r.Fail("Ən azı iki variant yazın (; ilə ayırın)", nameof(LabTest.Choices));
            if (model.ResultType == LabResultType.Choice && !string.IsNullOrWhiteSpace(model.RefText)
                && !model.ChoiceList.Contains(model.RefText.Trim(), StringComparer.OrdinalIgnoreCase))
                r.Fail("Normal dəyər variantlardan biri olmalıdır", nameof(LabTest.RefText));
            if (!r.Ok) return r;

            var isNew = model.Id == 0;
            var t = isNew ? new LabTest() : await _db.LabTests.FindAsync(new object[] { model.Id }, ct);
            if (t == null) return Result.Failure("Analiz tapılmadı");

            t.Code = model.Code;
            t.Name = model.Name;
            t.Category = Clean(model.Category);
            t.SampleType = Clean(model.SampleType) ?? "Qan";
            t.Unit = Clean(model.Unit);
            t.ResultType = model.ResultType;
            t.Choices = model.ResultType == LabResultType.Choice ? string.Join(";", model.ChoiceList) : null;
            var numeric = model.ResultType == LabResultType.Numeric;
            t.RefMin = numeric ? model.RefMin : null;
            t.RefMax = numeric ? model.RefMax : null;
            t.RefMinFemale = numeric ? model.RefMinFemale : null;
            t.RefMaxFemale = numeric ? model.RefMaxFemale : null;
            t.CriticalMin = numeric ? model.CriticalMin : null;
            t.CriticalMax = numeric ? model.CriticalMax : null;
            t.RefText = numeric ? null : Clean(model.RefText);
            t.SortOrder = model.SortOrder;
            t.IsActive = model.IsActive;
            if (isNew) _db.LabTests.Add(t);
            await _db.SaveChangesAsync(ct);

            _audit.Log(isNew ? "Analiz kataloqa əlavə edildi" : "Analiz dəyişdirildi", AuditEntities.Catalog, t.Id,
                $"{t.Code} {t.Name}; norma: {LabRef.Text(t, null)}");
            return Result.Success(isNew ? $"«{t.Name}» əlavə edildi" : $"«{t.Name}» yeniləndi", t.Id);
        }

        /// <summary>Adds the starter set (only tests whose code isn't in the catalog yet).</summary>
        public async Task<Result> ImportDefaultsAsync(CancellationToken ct = default)
        {
            var existing = (await _db.LabTests.Select(t => t.Code).ToListAsync(ct)).ToHashSet();
            var added = LabCatalog.Defaults().Where(t => !existing.Contains(t.Code)).ToList();
            _db.LabTests.AddRange(added);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Standart analizlər əlavə edildi", AuditEntities.Catalog, null, $"{added.Count} analiz");
            return Result.Success(added.Count == 0
                ? "Bütün standart analizlər artıq kataloqdadır"
                : $"{added.Count} analiz əlavə edildi. Norma aralıqlarını öz laboratoriyanızın reagentlərinə görə yoxlayın.");
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
