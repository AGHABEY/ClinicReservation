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
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");

            async Task Run()
            {
                await context.Database.MigrateAsync();
                await SeedStatusesAsync(context);
                await SeedWarehouseAsync(context);
                await SeedCancellationReasonsAsync(context);
                await SeedLoyaltyAsync(context);
                await SeedRolesAndAdminAsync(provider);
            }

            // Several instances starting together: one migrates, the others wait and then find nothing to do.
            try
            {
                await DbLock.RunExclusiveAsync(context.Database.GetConnectionString()!, DbLock.Migrations, Run);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.InvalidCatalogName)
            {
                // The database doesn't exist yet (first local run): nothing to lock on, MigrateAsync creates it.
                logger.LogInformation("Baza hələ yoxdur — yaradılır");
                await Run();
            }
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

        /// <summary>Stock needs at least one warehouse to receive into.</summary>
        private static async Task SeedWarehouseAsync(AppDbContext context)
        {
            if (await context.Warehouses.AnyAsync()) return;
            context.Warehouses.Add(new Warehouse { Name = "Mərkəzi anbar", IsMain = true });
            await context.SaveChangesAsync();
        }

        private static async Task SeedCancellationReasonsAsync(AppDbContext context)
        {
            if (await context.CancellationReasons.AnyAsync()) return;
            var order = 0;
            foreach (var (name, party) in BookingLabels.DefaultReasons)
                context.CancellationReasons.Add(new CancellationReason { Name = name, Party = party, SortOrder = order += 10 });
            await context.SaveChangesAsync();
        }

        /// <summary>Starting card levels and social discount categories (edited later on the Loyalty page).</summary>
        private static async Task SeedLoyaltyAsync(AppDbContext context)
        {
            if (!await context.LoyaltyTiers.AnyAsync())
                context.LoyaltyTiers.AddRange(
                    new LoyaltyTier { Name = "Standart", MinSpend = 0, DiscountPercent = 0, PointsPercent = 1 },
                    new LoyaltyTier { Name = "Gümüş", MinSpend = 500, DiscountPercent = 5, PointsPercent = 2 },
                    new LoyaltyTier { Name = "Qızıl", MinSpend = 1500, DiscountPercent = 10, PointsPercent = 3 },
                    new LoyaltyTier { Name = "Platin", MinSpend = 3000, DiscountPercent = 15, PointsPercent = 5 });
            if (!await context.BenefitCategories.AnyAsync())
                context.BenefitCategories.AddRange(
                    new BenefitCategory { Name = "Müharibə veteranı", Percent = 50 },
                    new BenefitCategory { Name = "Şəhid ailəsi", Percent = 50 },
                    new BenefitCategory { Name = "I qrup əlillik", Percent = 30 },
                    new BenefitCategory { Name = "II qrup əlillik", Percent = 20 },
                    new BenefitCategory { Name = "III qrup əlillik", Percent = 10 },
                    new BenefitCategory { Name = "Sağlamlıq imkanları məhdud uşaq", Percent = 30 },
                    new BenefitCategory { Name = "Təqaüdçü", Percent = 10 },
                    new BenefitCategory { Name = "Çoxuşaqlı ailə", Percent = 10 },
                    new BenefitCategory { Name = "Tələbə", Percent = 5 });
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

            // Manual discount limits: given once (when no role has one yet); after that only the Roles page changes them.
            if (!allRoleClaims.Any(x => x.Claims.Any(c => c.Type == Permissions.DiscountLimitClaim)))
                foreach (var (role, _) in allRoleClaims)
                    if (AppRoles.DefaultDiscountLimit(role.Name!) is var limit && limit > 0)
                        await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.DiscountLimitClaim, limit.ToString()));

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
