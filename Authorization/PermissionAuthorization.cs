using System.Security.Claims;
using ClinicReservation.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Authorization
{
    /// <summary>[HasPermission(Permissions.X)] — allows Admins and users whose role carries X.</summary>
    public sealed class HasPermissionAttribute : AuthorizeAttribute
    {
        public const string Prefix = "perm:";

        /// <param name="permissions">Any one of these is enough.</param>
        public HasPermissionAttribute(params string[] permissions) : base(Prefix + string.Join("|", permissions)) { }
    }

    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string[] permissions) => Permissions = permissions;
        public string[] Permissions { get; }
    }

    public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (requirement.Permissions.Any(p => context.User.Can(p)))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    /// <summary>Builds "perm:a|b" policies on demand so no policy has to be registered by hand.</summary>
    public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (!policyName.StartsWith(HasPermissionAttribute.Prefix, StringComparison.Ordinal))
                return await base.GetPolicyAsync(policyName);

            var permissions = policyName[HasPermissionAttribute.Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permissions))
                .Build();
        }
    }

    public static class PermissionExtensions
    {
        public static bool Can(this ClaimsPrincipal user, string permission) =>
            user.Identity?.IsAuthenticated == true &&
            (user.IsInRole(AppRoles.Admin) || user.HasClaim(Permissions.ClaimType, permission));

        public static bool CanAny(this ClaimsPrincipal user, params string[] permissions) =>
            permissions.Any(user.Can);
    }
}
