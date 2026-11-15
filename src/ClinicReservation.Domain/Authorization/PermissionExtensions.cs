using System.Security.Claims;
using ClinicReservation.Models;

namespace ClinicReservation.Authorization
{
    /// <summary>Permission checks on the signed-in user (Admin role always passes).</summary>
    public static class PermissionExtensions
    {
        public static bool Can(this ClaimsPrincipal user, string permission) =>
            user.Identity?.IsAuthenticated == true &&
            (user.IsInRole(AppRoles.Admin) || user.HasClaim(Permissions.ClaimType, permission));

        public static bool CanAny(this ClaimsPrincipal user, params string[] permissions) =>
            permissions.Any(user.Can);
    }
}
