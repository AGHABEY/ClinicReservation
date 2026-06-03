using ClinicReservation.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Data
{
    public class AppDbContext : IdentityDbContext
    {
        public AppDbContext(DbContextOptions options) : base(options)
        {

        }

        public DbSet<Reserve> Reserves { get; set; }
        public DbSet<ReserveExaminationType> ReserveExaminationTypes { get; set; }
        public DbSet<ReserveStatus> ReserveStatuses { get; set; }
        public DbSet<ExaminationType> ExaminationTypes { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<AppUser> AppUsers { get; set; }

    }
}
