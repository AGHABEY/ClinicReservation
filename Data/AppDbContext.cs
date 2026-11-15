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
        public DbSet<MedicalRecord> MedicalRecords { get; set; }
        public DbSet<SmsMessage> SmsMessages { get; set; }
        public DbSet<LabTest> LabTests { get; set; }
        public DbSet<ExaminationTypeLabTest> ExaminationTypeLabTests { get; set; }
        public DbSet<LabOrder> LabOrders { get; set; }
        public DbSet<LabResult> LabResults { get; set; }
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
                e.HasIndex(r => r.PatientToken).IsUnique();
                e.Property(r => r.PatientToken).HasMaxLength(64);
                e.Property(r => r.QueueTicket).HasMaxLength(16);
            });

            builder.Entity<Patient>(e =>
            {
                e.Property(p => p.Allergies).HasMaxLength(500);
                e.Property(p => p.ChronicConditions).HasMaxLength(500);
            });

            builder.Entity<Department>().Property(d => d.QueuePrefix).HasMaxLength(3);

            builder.Entity<MedicalRecord>(e =>
            {
                e.HasIndex(m => m.ReserveId).IsUnique();
                e.HasIndex(m => new { m.PatientId, m.CreatedAt });
                e.HasOne(m => m.Reserve).WithOne(r => r.MedicalRecord)
                    .HasForeignKey<MedicalRecord>(m => m.ReserveId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(m => m.Patient).WithMany(p => p.MedicalRecords)
                    .HasForeignKey(m => m.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(m => m.Doctor).WithMany()
                    .HasForeignKey(m => m.DoctorId).OnDelete(DeleteBehavior.SetNull);
                e.Property(m => m.IcdCode).HasMaxLength(10);
                e.Property(m => m.Diagnosis).HasMaxLength(300);
                foreach (var name in new[] { nameof(MedicalRecord.Complaint), nameof(MedicalRecord.Anamnesis), nameof(MedicalRecord.Examination),
                             nameof(MedicalRecord.Treatment), nameof(MedicalRecord.Prescription), nameof(MedicalRecord.Recommendations) })
                    e.Property(name).HasMaxLength(4000);
            });

            builder.Entity<LabTest>(e =>
            {
                e.HasIndex(t => t.Code).IsUnique();
                e.Property(t => t.Code).HasMaxLength(20);
                e.Property(t => t.Name).HasMaxLength(150);
                e.Property(t => t.Category).HasMaxLength(100);
                e.Property(t => t.SampleType).HasMaxLength(50);
                e.Property(t => t.Unit).HasMaxLength(30);
                e.Property(t => t.Choices).HasMaxLength(300);
                e.Property(t => t.RefText).HasMaxLength(150);
            });

            builder.Entity<ExaminationTypeLabTest>(e =>
            {
                e.HasKey(x => new { x.ExaminationTypeId, x.LabTestId });
                e.HasOne(x => x.ExaminationType).WithMany(x => x.LabTests)
                    .HasForeignKey(x => x.ExaminationTypeId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.LabTest).WithMany(x => x.ExaminationTypes)
                    .HasForeignKey(x => x.LabTestId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<LabOrder>(e =>
            {
                e.HasIndex(o => o.Barcode).IsUnique();
                e.HasIndex(o => o.CollectedAt);
                e.HasIndex(o => o.ReserveId);
                e.Property(o => o.Barcode).HasMaxLength(20);
                e.Property(o => o.Note).HasMaxLength(500);
                e.HasOne(o => o.Reserve).WithMany()
                    .HasForeignKey(o => o.ReserveId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(o => o.Patient).WithMany()
                    .HasForeignKey(o => o.PatientId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<LabResult>(e =>
            {
                e.HasIndex(r => new { r.LabOrderId, r.LabTestId }).IsUnique();
                e.Property(r => r.Value).HasMaxLength(500);
                e.Property(r => r.Unit).HasMaxLength(30);
                e.Property(r => r.Reference).HasMaxLength(150);
                e.HasOne(r => r.LabOrder).WithMany(o => o.Results)
                    .HasForeignKey(r => r.LabOrderId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(r => r.LabTest).WithMany()
                    .HasForeignKey(r => r.LabTestId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SmsMessage>(e =>
            {
                e.HasIndex(m => m.CreatedAt);
                e.HasOne(m => m.Reserve).WithMany()
                    .HasForeignKey(m => m.ReserveId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(m => m.Patient).WithMany()
                    .HasForeignKey(m => m.PatientId).OnDelete(DeleteBehavior.SetNull);
                e.Property(m => m.Phone).HasMaxLength(32);
                e.Property(m => m.Text).HasMaxLength(1000);
                e.Property(m => m.Kind).HasMaxLength(32);
                e.Property(m => m.Error).HasMaxLength(500);
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
