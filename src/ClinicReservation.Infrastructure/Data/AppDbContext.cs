using ClinicReservation.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Data
{
    public class AppDbContext : IdentityDbContext, IDataProtectionKeyContext, IAppDbContext
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
        public DbSet<StockItem> StockItems { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<StockBatch> StockBatches { get; set; }
        public DbSet<StockDocument> StockDocuments { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<ExaminationTypeStockItem> ExaminationTypeStockItems { get; set; }
        public DbSet<Ward> Wards { get; set; }
        public DbSet<Bed> Beds { get; set; }
        public DbSet<Admission> Admissions { get; set; }
        public DbSet<BedStay> BedStays { get; set; }
        public DbSet<Observation> Observations { get; set; }
        public DbSet<MedicationOrder> MedicationOrders { get; set; }
        public DbSet<MedicationDose> MedicationDoses { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<Equipment> Equipment { get; set; }
        public DbSet<CancellationReason> CancellationReasons { get; set; }
        public DbSet<AppUser> AppUsers { get; set; }

        /// <summary>Keys that sign login cookies and form tokens — shared by every app instance.</summary>
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

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

            builder.Entity<StockItem>(e =>
            {
                e.HasIndex(i => i.Code).IsUnique();
                e.HasIndex(i => i.Name);
                e.Property(i => i.Name).HasMaxLength(200);
                e.Property(i => i.Code).HasMaxLength(50);
                e.Property(i => i.Form).HasMaxLength(100);
                e.Property(i => i.Unit).HasMaxLength(20);
                e.Property(i => i.Note).HasMaxLength(500);
                e.Property(i => i.MinQuantity).HasPrecision(18, 3);
            });

            builder.Entity<Warehouse>(e =>
            {
                e.Property(w => w.Name).HasMaxLength(100);
                e.HasOne(w => w.Department).WithMany()
                    .HasForeignKey(w => w.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<Supplier>(e =>
            {
                e.Property(s => s.Name).HasMaxLength(200);
                e.Property(s => s.Voen).HasMaxLength(20);
                e.Property(s => s.Phone).HasMaxLength(32);
                e.Property(s => s.Note).HasMaxLength(500);
            });

            builder.Entity<StockBatch>(e =>
            {
                e.HasIndex(b => new { b.StockItemId, b.WarehouseId });
                e.HasIndex(b => b.ExpiryDate);
                e.Property(b => b.BatchNo).HasMaxLength(50);
                e.Property(b => b.Quantity).HasPrecision(18, 3);
                e.Property(b => b.UnitCost).HasPrecision(18, 4);
                e.HasOne(b => b.StockItem).WithMany(i => i.Batches)
                    .HasForeignKey(b => b.StockItemId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(b => b.Warehouse).WithMany()
                    .HasForeignKey(b => b.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<StockDocument>(e =>
            {
                e.HasIndex(d => d.Date);
                e.HasIndex(d => d.ReserveId);
                e.Property(d => d.Number).HasMaxLength(20);
                e.Property(d => d.InvoiceNo).HasMaxLength(50);
                e.Property(d => d.Reason).HasMaxLength(100);
                e.Property(d => d.Note).HasMaxLength(1000);
                e.Property(d => d.CancelReason).HasMaxLength(300);
                e.HasOne(d => d.FromWarehouse).WithMany()
                    .HasForeignKey(d => d.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(d => d.ToWarehouse).WithMany()
                    .HasForeignKey(d => d.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(d => d.Supplier).WithMany()
                    .HasForeignKey(d => d.SupplierId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(d => d.Patient).WithMany()
                    .HasForeignKey(d => d.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(d => d.Reserve).WithMany()
                    .HasForeignKey(d => d.ReserveId).OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(d => d.AdmissionId);
                e.HasOne(d => d.Admission).WithMany()
                    .HasForeignKey(d => d.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Ward>(e =>
            {
                e.Property(w => w.Name).HasMaxLength(100);
                e.Property(w => w.DailyPrice).HasPrecision(18, 2);
                e.HasOne(w => w.Department).WithMany()
                    .HasForeignKey(w => w.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<Bed>(e =>
            {
                e.Property(b => b.Name).HasMaxLength(20);
                e.HasIndex(b => new { b.WardId, b.Name }).IsUnique();
                e.HasOne(b => b.Ward).WithMany(w => w.Beds)
                    .HasForeignKey(b => b.WardId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Admission>(e =>
            {
                e.HasIndex(a => a.Status);
                e.HasIndex(a => a.AdmittedAt);
                e.HasIndex(a => a.PatientId, "IX_Admissions_PatientId");
                // One active stay per bed and per patient, enforced by the database.
                e.HasIndex(a => a.BedId, "IX_Admissions_BedId_Active").IsUnique().HasFilter("\"Status\" = 1");
                e.HasIndex(a => a.PatientId, "IX_Admissions_PatientId_Active").IsUnique().HasFilter("\"Status\" = 1");
                e.Property(a => a.Number).HasMaxLength(20);
                e.Property(a => a.AdmitIcdCode).HasMaxLength(10);
                e.Property(a => a.FinalIcdCode).HasMaxLength(10);
                e.Property(a => a.AdmitDiagnosis).HasMaxLength(300);
                e.Property(a => a.FinalDiagnosis).HasMaxLength(300);
                e.Property(a => a.AdmitNote).HasMaxLength(2000);
                e.Property(a => a.Course).HasMaxLength(4000);
                e.Property(a => a.TreatmentSummary).HasMaxLength(4000);
                e.Property(a => a.Recommendations).HasMaxLength(4000);
                e.Property(a => a.CancelReason).HasMaxLength(300);
                e.HasOne(a => a.Patient).WithMany()
                    .HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(a => a.Bed).WithMany()
                    .HasForeignKey(a => a.BedId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(a => a.Department).WithMany()
                    .HasForeignKey(a => a.DepartmentId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(a => a.Doctor).WithMany()
                    .HasForeignKey(a => a.DoctorId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(a => a.Reserve).WithMany()
                    .HasForeignKey(a => a.ReserveId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<BedStay>(e =>
            {
                e.Property(s => s.DailyPrice).HasPrecision(18, 2);
                e.HasOne(s => s.Admission).WithMany(a => a.Stays)
                    .HasForeignKey(s => s.AdmissionId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(s => s.Bed).WithMany()
                    .HasForeignKey(s => s.BedId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Observation>(e =>
            {
                e.HasIndex(o => new { o.AdmissionId, o.At });
                e.Property(o => o.Text).HasMaxLength(4000);
                e.Property(o => o.Temperature).HasPrecision(4, 1);
                e.HasOne(o => o.Admission).WithMany(a => a.Observations)
                    .HasForeignKey(o => o.AdmissionId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<MedicationOrder>(e =>
            {
                e.Property(o => o.DrugName).HasMaxLength(200);
                e.Property(o => o.Dose).HasMaxLength(50);
                e.Property(o => o.Route).HasMaxLength(50);
                e.Property(o => o.Times).HasMaxLength(100);
                e.Property(o => o.Note).HasMaxLength(500);
                e.Property(o => o.QuantityPerDose).HasPrecision(18, 3);
                e.HasOne(o => o.Admission).WithMany(a => a.Orders)
                    .HasForeignKey(o => o.AdmissionId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(o => o.StockItem).WithMany()
                    .HasForeignKey(o => o.StockItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<MedicationDose>(e =>
            {
                // A scheduled dose is recorded once.
                e.HasIndex(d => new { d.MedicationOrderId, d.ScheduledAt }).IsUnique();
                e.Property(d => d.Note).HasMaxLength(300);
                e.HasOne(d => d.MedicationOrder).WithMany(o => o.Doses)
                    .HasForeignKey(d => d.MedicationOrderId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<StockMovement>(e =>
            {
                e.HasIndex(m => new { m.StockItemId, m.At });
                e.HasIndex(m => new { m.WarehouseId, m.At });
                e.Property(m => m.Quantity).HasPrecision(18, 3);
                e.Property(m => m.Counted).HasPrecision(18, 3);
                e.Property(m => m.UnitCost).HasPrecision(18, 4);
                e.HasOne(m => m.StockDocument).WithMany(d => d.Movements)
                    .HasForeignKey(m => m.StockDocumentId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(m => m.StockItem).WithMany()
                    .HasForeignKey(m => m.StockItemId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(m => m.StockBatch).WithMany()
                    .HasForeignKey(m => m.StockBatchId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(m => m.Warehouse).WithMany()
                    .HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ExaminationTypeStockItem>(e =>
            {
                e.HasKey(x => new { x.ExaminationTypeId, x.StockItemId });
                e.Property(x => x.Quantity).HasPrecision(18, 3);
                e.HasOne(x => x.ExaminationType).WithMany(x => x.Materials)
                    .HasForeignKey(x => x.ExaminationTypeId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.StockItem).WithMany()
                    .HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Equipment>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100);
                e.Property(x => x.Note).HasMaxLength(300);
                e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            });
            builder.Entity<ExaminationType>().HasOne(x => x.Equipment).WithMany()
                .HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.SetNull);

            builder.Entity<CancellationReason>().Property(x => x.Name).HasMaxLength(150);
            builder.Entity<Reserve>(e =>
            {
                e.HasOne(r => r.CancellationReason).WithMany().HasForeignKey(r => r.CancellationReasonId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(r => r.Equipment).WithMany().HasForeignKey(r => r.EquipmentId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(r => new { r.EquipmentId, r.AppointmentAt });
            });

            builder.Entity<OutboxMessage>(e =>
            {
                e.Property(m => m.Type).HasMaxLength(100);
                e.Property(m => m.LastError).HasMaxLength(1000);
                // The worker looks for unprocessed messages only.
                e.HasIndex(m => m.OccurredAt).HasFilter("\"ProcessedAt\" IS NULL");
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

            // Each module owns its tables in its own PostgreSQL schema (Identity and data-protection
            // keys stay in "public"). A module that is later moved out takes its schema with it.
            void Schema(string schema, params Type[] types)
            {
                foreach (var t in types)
                    builder.Entity(t).ToTable(builder.Model.FindEntityType(t)!.GetTableName(), schema);
            }
            Schema("core", typeof(Patient), typeof(Department), typeof(Room), typeof(Equipment), typeof(Doctor), typeof(DoctorWorkHours), typeof(DoctorLeave), typeof(ExaminationType));
            Schema("scheduling", typeof(Reserve), typeof(ReserveStatus), typeof(ReserveExaminationType), typeof(CancellationReason));
            Schema("billing", typeof(Payment));
            Schema("emr", typeof(MedicalRecord));
            Schema("lab", typeof(LabTest), typeof(ExaminationTypeLabTest), typeof(LabOrder), typeof(LabResult));
            Schema("stock", typeof(StockItem), typeof(Warehouse), typeof(Supplier), typeof(StockBatch), typeof(StockDocument), typeof(StockMovement), typeof(ExaminationTypeStockItem));
            Schema("inpatient", typeof(Ward), typeof(Bed), typeof(Admission), typeof(BedStay), typeof(Observation), typeof(MedicationOrder), typeof(MedicationDose));
            Schema("notify", typeof(SmsMessage));
            Schema("audit", typeof(AuditLog));
            Schema("platform", typeof(OutboxMessage));
        }
    }
}
