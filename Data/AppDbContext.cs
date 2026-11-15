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

        public DbSet<Patient> Patients { get; set; }
        public DbSet<Reserve> Reserves { get; set; }
        public DbSet<ReserveExaminationType> ReserveExaminationTypes { get; set; }
        public DbSet<ReserveStatus> ReserveStatuses { get; set; }
        public DbSet<ExaminationType> ExaminationTypes { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Room> Rooms { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<DoctorWorkHours> DoctorWorkHours { get; set; }
        public DbSet<DoctorLeave> DoctorLeaves { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<AppUser> AppUsers { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Patient>(e =>
            {
                e.HasIndex(p => p.PinCode).IsUnique();
                e.HasIndex(p => new { p.Surname, p.Name });
            });

            builder.Entity<Reserve>(e =>
            {
                e.HasIndex(r => r.AppointmentAt);
                e.HasIndex(r => new { r.DoctorId, r.AppointmentAt });
                e.HasOne(r => r.Patient).WithMany(p => p.Reserves)
                    .HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(r => r.Doctor).WithMany()
                    .HasForeignKey(r => r.DoctorId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(r => r.Room).WithMany()
                    .HasForeignKey(r => r.RoomId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Doctor>(e =>
            {
                e.HasOne(d => d.Department).WithMany(dp => dp.Doctors)
                    .HasForeignKey(d => d.DepartmentId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(d => d.Room).WithMany()
                    .HasForeignKey(d => d.RoomId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<DoctorWorkHours>().HasIndex(w => new { w.DoctorId, w.Day }).IsUnique();

            builder.Entity<ExaminationType>().HasOne(x => x.Department).WithMany()
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);

            builder.Entity<AuditLog>(e =>
            {
                e.HasIndex(a => a.At);
                e.HasIndex(a => new { a.EntityType, a.EntityId });
            });
        }
    }
}
