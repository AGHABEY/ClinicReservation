using ClinicReservation.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Data
{
    /// <summary>
    /// Applies pending migrations and seeds the database with the data the
    /// application needs to run (reservation statuses, the Admin role and a
    /// default administrator account).
    /// </summary>
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;

            var context = provider.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync();

            await SeedStatusesAsync(context);
            await SeedRolesAndAdminAsync(provider);
        }

        private static async Task SeedStatusesAsync(AppDbContext context)
        {
            // Add any status that is missing, so new statuses also reach existing databases.
            var existing = await context.ReserveStatuses.Select(s => s.Name).ToListAsync();
            foreach (var name in StatusNames.All)
            {
                if (!existing.Contains(name))
                    context.ReserveStatuses.Add(new ReserveStatus { Name = name });
            }
            await context.SaveChangesAsync();
        }

        private static async Task SeedRolesAndAdminAsync(IServiceProvider provider)
        {
            var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();

            var createdNow = new HashSet<string>();
            foreach (var roleName in AppRoles.BuiltIn)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role == null)
                {
                    role = new IdentityRole(roleName);
                    await roleManager.CreateAsync(role);
                    createdNow.Add(roleName);
                }

                // Give a role its default permissions only once (when it has none),
                // so later edits on the Roles page are never overwritten.
                var claims = await roleManager.GetClaimsAsync(role);
                if (!claims.Any(c => c.Type == Permissions.ClaimType))
                {
                    foreach (var permission in AppRoles.DefaultPermissions(roleName))
                        await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));
                }
            }

            // Permissions introduced in a later version: if no role carries one yet,
            // give it to the built-in roles that have it by default. Roles created in
            // this run already got their defaults and don't count as "someone has it". After that the
            // Roles page is the only place that changes it.
            var allRoleClaims = new List<(IdentityRole Role, IList<System.Security.Claims.Claim> Claims)>();
            foreach (var roleName in AppRoles.BuiltIn)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role != null) allRoleClaims.Add((role, await roleManager.GetClaimsAsync(role)));
            }
            var customRoles = roleManager.Roles.Where(r => !AppRoles.BuiltIn.Contains(r.Name!)).ToList();
            foreach (var role in customRoles)
                allRoleClaims.Add((role, await roleManager.GetClaimsAsync(role)));

            foreach (var permission in Permissions.All.Select(p => p.Code))
            {
                var anyoneHasIt = allRoleClaims.Where(x => !createdNow.Contains(x.Role.Name!))
                    .Any(x => x.Claims.Any(c => c.Type == Permissions.ClaimType && c.Value == permission));
                if (anyoneHasIt) continue;
                var targets = allRoleClaims.Where(x => AppRoles.DefaultPermissions(x.Role.Name!).Contains(permission)
                    && !x.Claims.Any(c => c.Type == Permissions.ClaimType && c.Value == permission)).ToList();
                foreach (var (role, _) in targets)
                    await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));
            }

            await EnsureUserAsync(userManager, "admin", "admin123", "Administrator", "admin@clinic.local", AppRoles.Admin);
            await EnsureUserAsync(userManager, "kassir", "kassir123", "Kassir", "kassir@clinic.local", AppRoles.Staff);
        }

        private static async Task EnsureUserAsync(UserManager<IdentityUser> userManager,
            string userName, string password, string fullName, string email, string role)
        {
            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                user = new AppUser
                {
                    UserName = userName,
                    Email = email,
                    EmailConfirmed = true,
                    FullName = fullName
                };
                var result = await userManager.CreateAsync(user, password);
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(user, role);
            }
        }
    }
}
