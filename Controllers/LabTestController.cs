using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    /// <summary>Analiz kataloqu: parameters, units and reference ranges.</summary>
    [HasPermission(Permissions.CatalogManage)]
    public class LabTestController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public LabTestController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public IActionResult Index()
        {
            var tests = _context.LabTests
                .Include(t => t.ExaminationTypes).ThenInclude(x => x.ExaminationType)
                .OrderBy(t => t.Category).ThenBy(t => t.SortOrder).ThenBy(t => t.Name)
                .ToList();
            return View(tests);
        }

        public IActionResult Create() => View("Form", new LabTest { SortOrder = (_context.LabTests.Max(t => (int?)t.SortOrder) ?? 0) + 10 });

        public IActionResult Edit(int id)
        {
            var t = _context.LabTests.Find(id);
            return t == null ? NotFound() : View("Form", t);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Save(LabTest model)
        {
            // Our own (Azerbaijani) checks below replace the implicit "required" errors.
            foreach (var key in new[] { nameof(model.ExaminationTypes), nameof(model.Code), nameof(model.Name), nameof(model.SampleType) })
                ModelState.Remove(key);
            model.Code = (model.Code ?? "").Trim().ToUpperInvariant();
            model.Name = (model.Name ?? "").Trim();

            if (string.IsNullOrEmpty(model.Code)) ModelState.AddModelError(nameof(model.Code), "Kod daxil edin");
            else if (model.Code.Length > 20) ModelState.AddModelError(nameof(model.Code), "Kod ən çox 20 simvol");
            else if (_context.LabTests.Any(t => t.Code == model.Code && t.Id != model.Id))
                ModelState.AddModelError(nameof(model.Code), "Bu kodla analiz artıq var");
            if (string.IsNullOrEmpty(model.Name)) ModelState.AddModelError(nameof(model.Name), "Ad daxil edin");
            if (model.RefMin > model.RefMax) ModelState.AddModelError(nameof(model.RefMax), "Yuxarı hədd aşağı həddən kiçikdir");
            if (model.RefMinFemale > model.RefMaxFemale) ModelState.AddModelError(nameof(model.RefMaxFemale), "Yuxarı hədd aşağı həddən kiçikdir");
            if (model.ResultType == LabResultType.Choice && model.ChoiceList.Length < 2)
                ModelState.AddModelError(nameof(model.Choices), "Ən azı iki variant yazın (; ilə ayırın)");
            if (model.ResultType == LabResultType.Choice && !string.IsNullOrWhiteSpace(model.RefText)
                && !model.ChoiceList.Contains(model.RefText.Trim(), StringComparer.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(model.RefText), "Normal dəyər variantlardan biri olmalıdır");

            if (!ModelState.IsValid)
                return View("Form", model);

            var isNew = model.Id == 0;
            var t = isNew ? new LabTest() : _context.LabTests.Find(model.Id);
            if (t == null) return NotFound();

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
            if (isNew) _context.LabTests.Add(t);
            _context.SaveChanges();

            _audit.Log(isNew ? "Analiz kataloqa əlavə edildi" : "Analiz dəyişdirildi", AuditEntities.Catalog, t.Id,
                $"{t.Code} {t.Name}; norma: {LabRef.Text(t, null)}");
            TempData["Success"] = isNew ? $"«{t.Name}» əlavə edildi" : $"«{t.Name}» yeniləndi";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Adds the starter set (only tests whose code isn't in the catalog yet).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ImportDefaults()
        {
            var existing = _context.LabTests.Select(t => t.Code).ToHashSet();
            var added = LabCatalog.Defaults().Where(t => !existing.Contains(t.Code)).ToList();
            _context.LabTests.AddRange(added);
            _context.SaveChanges();
            _audit.Log("Standart analizlər əlavə edildi", AuditEntities.Catalog, null, $"{added.Count} analiz");
            TempData["Success"] = added.Count == 0
                ? "Bütün standart analizlər artıq kataloqdadır"
                : $"{added.Count} analiz əlavə edildi. Norma aralıqlarını öz laboratoriyanızın reagentlərinə görə yoxlayın.";
            return RedirectToAction(nameof(Index));
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
