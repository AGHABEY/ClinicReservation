using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Lab;
using ClinicReservation.Web;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Analiz kataloqu: parameters, units and reference ranges.</summary>
    [HasPermission(Permissions.CatalogManage)]
    public class LabTestController : Controller
    {
        private readonly LabQueries _queries;
        private readonly LabCatalogSettings _settings;

        public LabTestController(LabQueries queries, LabCatalogSettings settings)
        {
            _queries = queries;
            _settings = settings;
        }

        public async Task<IActionResult> Index(CancellationToken ct) => View(await _queries.CatalogAsync(ct));

        public async Task<IActionResult> Create(CancellationToken ct) =>
            View("Form", new LabTest { SortOrder = await _queries.NextSortOrderAsync(ct) });

        public async Task<IActionResult> Edit(int id, CancellationToken ct) =>
            await _queries.TestAsync(id, ct) is { } t ? View("Form", t) : NotFound();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(LabTest model, CancellationToken ct)
        {
            // Our own (Azerbaijani) checks replace the implicit "required" errors.
            foreach (var key in new[] { nameof(model.ExaminationTypes), nameof(model.Code), nameof(model.Name), nameof(model.SampleType) })
                ModelState.Remove(key);
            if (ModelState.IsValid)
            {
                var r = await _settings.SaveAsync(model, ct);
                if (r.Ok)
                {
                    TempData["Success"] = r.Message;
                    return RedirectToAction(nameof(Index));
                }
                ModelState.AddErrors(r);
            }
            return View("Form", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportDefaults(CancellationToken ct)
        {
            TempData["Success"] = (await _settings.ImportDefaultsAsync(ct)).Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
