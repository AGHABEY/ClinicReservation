using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules;
using ClinicReservation.Modules.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Şöbələr və kabinetlər.</summary>
    [HasPermission(Permissions.CatalogManage)]
    public class DepartmentController : Controller
    {
        private readonly ClinicSettings _settings;
        public DepartmentController(ClinicSettings settings) => _settings = settings;

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var (departments, rooms) = await _settings.DepartmentsPageAsync(ct);
            ViewBag.Departments = departments;
            ViewBag.Rooms = rooms;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDepartment(int id, string? name, string? queuePrefix, CancellationToken ct, bool isActive = true) =>
            Back(await _settings.SaveDepartmentAsync(id, name, queuePrefix, isActive, ct));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRoom(int id, string? name, CancellationToken ct, bool isActive = true) =>
            Back(await _settings.SaveRoomAsync(id, name, isActive, ct));

        private IActionResult Back(Result r)
        {
            TempData[r.Ok ? "Success" : "Error"] = r.Ok ? r.Message : r.ErrorText;
            return RedirectToAction(nameof(Index));
        }
    }
}
