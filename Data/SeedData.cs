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

            const string adminRole = "Admin";
            if (!await roleManager.RoleExistsAsync(adminRole))
            {
                await roleManager.CreateAsync(new IdentityRole(adminRole));
            }

            const string adminUserName = "admin";
            var admin = await userManager.FindByNameAsync(adminUserName);
            if (admin == null)
            {
                admin = new AppUser
                {
                    UserName = adminUserName,
                    Email = "admin@clinic.local",
                    EmailConfirmed = true,
                    FullName = "Administrator"
                };
                var result = await userManager.CreateAsync(admin, "admin123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, adminRole);
                }
            }
        }
    }
}
