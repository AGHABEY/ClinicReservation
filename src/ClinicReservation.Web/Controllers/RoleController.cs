using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    /// <summary>Role × permission matrix. Admin always has everything and isn't editable.</summary>
    [HasPermission(Permissions.UsersManage)]
    public class RoleController : Controller
    {
        private readonly RoleManager<IdentityRole> _roles;
        private readonly UserManager<IdentityUser> _users;
        private readonly AuditService _audit;

        public RoleController(RoleManager<IdentityRole> roles, UserManager<IdentityUser> users, AuditService audit)
        {
            _roles = roles;
            _users = users;
            _audit = audit;
        }

        public async Task<IActionResult> Index()
        {
            var model = new List<RoleRow>();
            foreach (var role in _roles.Roles.ToList().OrderBy(r => Array.IndexOf(AppRoles.BuiltIn, r.Name) is var i && i < 0 ? 99 : i).ThenBy(r => r.Name))
            {
                var claims = await _roles.GetClaimsAsync(role);
                model.Add(new RoleRow
                {
                    Role = role,
                    Permissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToHashSet(),
                    DiscountLimit = claims.Where(c => c.Type == Permissions.DiscountLimitClaim).Select(c => int.TryParse(c.Value, out var v) ? v : 0).DefaultIfEmpty(0).Max(),
                    UserCount = (await _users.GetUsersInRoleAsync(role.Name!)).Count
                });
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(string roleId, List<string>? permissions, int? discountLimit)
        {
            var role = await _roles.FindByIdAsync(roleId);
            if (role == null) return NotFound();
            if (role.Name == AppRoles.Admin)
            {
                TempData["Error"] = "Administrator rolunun icazələri dəyişdirilmir — onun hər şeyə icazəsi var";
                return RedirectToAction(nameof(Index));
            }

            var valid = Permissions.All.Select(p => p.Code).ToHashSet();
            var wanted = (permissions ?? new()).Where(valid.Contains).ToHashSet();
            var current = (await _roles.GetClaimsAsync(role)).Where(c => c.Type == Permissions.ClaimType).ToList();

            foreach (var c in current.Where(c => !wanted.Contains(c.Value)))
                await _roles.RemoveClaimAsync(role, c);
            foreach (var p in wanted.Where(p => current.All(c => c.Value != p)))
                await _roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, p));

            // Manual discount limit: one claim, 0 = none.
            var limit = Math.Clamp(discountLimit ?? 0, 0, 100);
            var oldLimits = (await _roles.GetClaimsAsync(role)).Where(c => c.Type == Permissions.DiscountLimitClaim).ToList();
            var oldLimit = oldLimits.Select(c => int.TryParse(c.Value, out var v) ? v : 0).DefaultIfEmpty(0).Max();
            if (oldLimit != limit || oldLimits.Count > 1)
            {
                foreach (var c in oldLimits) await _roles.RemoveClaimAsync(role, c);
                if (limit > 0) await _roles.AddClaimAsync(role, new Claim(Permissions.DiscountLimitClaim, limit.ToString()));
            }

            // Make signed-in members pick up the change on their next request cycle.
            foreach (var user in await _users.GetUsersInRoleAsync(role.Name!))
                await _users.UpdateSecurityStampAsync(user);

            var added = wanted.Except(current.Select(c => c.Value)).ToList();
            var removed = current.Select(c => c.Value).Except(wanted).ToList();
            _audit.Log("Rol icazələri dəyişdirildi", AuditEntities.Role, role.Name,
                $"{AppRoles.Label(role.Name)}: +[{string.Join(", ", added)}] −[{string.Join(", ", removed)}]" + (oldLimit != limit ? $", endirim limiti {oldLimit}% → {limit}%" : ""));

            TempData["Success"] = $"«{AppRoles.Label(role.Name)}» rolunun icazələri yeniləndi";
            return RedirectToAction(nameof(Index));
        }
    }

    public class RoleRow
    {
        public IdentityRole Role { get; set; } = null!;
        public HashSet<string> Permissions { get; set; } = new();
        public int UserCount { get; set; }
        public int DiscountLimit { get; set; }
    }
}
