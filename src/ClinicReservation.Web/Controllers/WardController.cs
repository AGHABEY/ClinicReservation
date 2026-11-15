using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Inpatient;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Palatalar və çarpayılar.</summary>
    [HasPermission(Permissions.InpatientManage)]
    public class WardController : Controller
    {
        private readonly InpatientQueries _queries;
        private readonly WardSettings _settings;

        public WardController(InpatientQueries queries, WardSettings settings)
        {
            _queries = queries;
            _settings = settings;
        }

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var (wards, departments, occupied) = await _queries.WardsPageAsync(ct);
            ViewBag.Departments = departments;
            ViewBag.Occupied = occupied;
            return View(wards);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(int id, string? name, int? departmentId, WardGender gender, decimal dailyPrice, int sortOrder,
            CancellationToken ct, bool isActive = true, int bedCount = 0) =>
            Back(await _settings.SaveWardAsync(id, name, departmentId, gender, dailyPrice, sortOrder, isActive, bedCount, ct));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddBed(int wardId, string? name, CancellationToken ct) =>
            await _settings.AddBedAsync(wardId, name, ct) is { } r ? Back(r) : NotFound();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBed(int id, CancellationToken ct) =>
            await _settings.ToggleBedAsync(id, ct) is { } r ? Back(r) : NotFound();

        private IActionResult Back(Result r)
        {
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Index));
        }
    }
}
