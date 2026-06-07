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
            if (!await context.ReserveStatuses.AnyAsync())
            {
                context.ReserveStatuses.AddRange(
                    new ReserveStatus { Name = "Waiting" },
                    new ReserveStatus { Name = "Confirmed" });
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedRolesAndAdminAsync(IServiceProvider provider)
        {
            var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();

            foreach (var role in new[] { AppRoles.Admin, AppRoles.Staff })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
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
