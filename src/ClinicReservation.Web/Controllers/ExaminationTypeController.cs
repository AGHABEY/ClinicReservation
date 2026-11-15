using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Catalog;
using ClinicReservation.Modules.Pharmacy;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.CatalogManage, Permissions.ReservationsView)]
    public class ExaminationTypeController : Controller
    {
        private readonly ClinicSettings _settings;
        public ExaminationTypeController(ClinicSettings settings) => _settings = settings;

        public async Task<IActionResult> Index(CancellationToken ct) => View(await _settings.ExaminationsAsync(ct));

        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> Add(CancellationToken ct)
        {
            await Lists(null, new(), new(), ct);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> Add(ExaminationType model, List<int>? labTestIds, List<MaterialInput>? materialRows, CancellationToken ct)
        {
            var r = await _settings.SaveExaminationAsync(null, model, labTestIds, materialRows, FormErrors(model), ct);
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Index));
            }
            ShowErrors(r);
            await Lists(null, labTestIds ?? new(), materialRows ?? new(), ct);
            return View(model);
        }

        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> Edit(int id, CancellationToken ct)
        {
            var et = await _settings.ExaminationAsync(id, ct);
            if (et == null) return NotFound();
            await Lists(et.DepartmentId, et.LabTests.Select(l => l.LabTestId).ToList(),
                et.Materials.Select(m => new MaterialInput { StockItemId = m.StockItemId, Quantity = m.Quantity }).ToList(), ct);
            return View(et);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.CatalogManage)]
        public async Task<IActionResult> Edit(ExaminationType model, List<int>? labTestIds, List<MaterialInput>? materialRows, CancellationToken ct)
        {
            var existing = await _settings.ExaminationAsync(model.Id, ct);
            if (existing == null) return NotFound();
            var r = await _settings.SaveExaminationAsync(existing, model, labTestIds, materialRows, FormErrors(model), ct);
            if (r.Ok)
            {
                TempData["Success"] = r.Message;
                return RedirectToAction(nameof(Index));
            }
            ShowErrors(r);
            await Lists(existing.DepartmentId, labTestIds ?? new(), materialRows ?? new(), ct);
            return View(model);
        }

        private List<(string? Field, string Message)> FormErrors(ExaminationType model)
        {
            ModelState.Remove(nameof(model.Department));
            ModelState.Remove(nameof(model.LabTests));
            ModelState.Remove(nameof(model.Materials));
            return ModelState.SelectMany(kv => kv.Value!.Errors.Select(e => ((string?)kv.Key, e.ErrorMessage))).ToList();
        }

        private void ShowErrors(ClinicReservation.Modules.Result r)
        {
            foreach (var (field, message) in r.Errors)
            {
                var key = field ?? "";
                if (ModelState.TryGetValue(key, out var entry) && entry.Errors.Any(e => e.ErrorMessage == message)) continue;
                ModelState.AddModelError(key, message);
            }
        }

        private async Task Lists(int? keepDepartmentId, List<int> labTests, List<MaterialInput> materials, CancellationToken ct)
        {
            var lists = await _settings.ExamFormListsAsync(keepDepartmentId, labTests, materials, ct);
            ViewBag.Departments = lists.Departments;
            ViewBag.LabTests = lists.LabTests;
            ViewBag.SelectedLabTests = lists.SelectedLabTests;
            ViewBag.StockItems = lists.StockItems;
            ViewBag.Equipment = lists.Equipment;
            ViewBag.Materials = materials.Where(m => m.StockItemId != null).ToList();
        }
    }
}
