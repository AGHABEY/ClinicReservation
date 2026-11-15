using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [HasPermission(Permissions.AuditView)]
    public class AuditController : Controller
    {
        private const int PageSize = 50;
        private readonly AppDbContext _context;

        public AuditController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index(string? entity, string? entityId, string? q, DateTime? from, DateTime? to, int page = 1)
        {
            var query = _context.AuditLogs.AsQueryable();
            if (!string.IsNullOrEmpty(entity))
                query = query.Where(a => a.EntityType == entity);
            if (!string.IsNullOrEmpty(entityId))
                query = query.Where(a => a.EntityId == entityId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(a => EF.Functions.ILike(a.UserName ?? "", term)
                    || EF.Functions.ILike(a.Action, term)
                    || EF.Functions.ILike(a.Details ?? "", term));
            }
            if (from.HasValue) query = query.Where(a => a.At >= from.Value.Date);
            if (to.HasValue) query = query.Where(a => a.At < to.Value.Date.AddDays(1));

            var total = query.Count();
            page = Math.Max(1, page);
            var rows = query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
                .Skip((page - 1) * PageSize).Take(PageSize).ToList();

            ViewBag.Entities = _context.AuditLogs.Where(a => a.EntityType != null).Select(a => a.EntityType!).Distinct().OrderBy(x => x).ToList();
            ViewBag.Entity = entity;
            ViewBag.EntityId = entityId;
            ViewBag.Query = q;
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.PageSize = PageSize;
            return View(rows);
        }
    }
}
