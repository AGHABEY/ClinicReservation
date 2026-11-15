using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Modules.Audit;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.AuditView)]
    public class AuditController : Controller
    {
        private readonly AuditQueries _audit;
        public AuditController(AuditQueries audit) => _audit = audit;

        public async Task<IActionResult> Index(string? entity, string? entityId, string? q, DateTime? from, DateTime? to, CancellationToken ct, int page = 1)
        {
            page = Math.Max(1, page);
            var (rows, total, entities) = await _audit.SearchAsync(entity, entityId, q, from, to, page, ct);
            ViewBag.Entities = entities;
            ViewBag.Entity = entity;
            ViewBag.EntityId = entityId;
            ViewBag.Query = q;
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.PageSize = AuditQueries.PageSize;
            return View(rows);
        }
    }
}
