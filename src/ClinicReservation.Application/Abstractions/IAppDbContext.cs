using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

// Kept in the ClinicReservation.Data namespace so services read the same as before the split.
namespace ClinicReservation.Data
{
    /// <summary>
    /// What the application layer needs from the database. Implemented by AppDbContext
    /// (Infrastructure), so services don't depend on PostgreSQL, Identity or EF configuration.
    /// </summary>
    public interface IAppDbContext
    {
        DbSet<Patient> Patients { get; }
        DbSet<Reserve> Reserves { get; }
        DbSet<ReserveExaminationType> ReserveExaminationTypes { get; }
        DbSet<ReserveStatus> ReserveStatuses { get; }
        DbSet<ExaminationType> ExaminationTypes { get; }
        DbSet<Payment> Payments { get; }
        DbSet<Department> Departments { get; }
        DbSet<Room> Rooms { get; }
        DbSet<Doctor> Doctors { get; }
        DbSet<DoctorWorkHours> DoctorWorkHours { get; }
        DbSet<DoctorLeave> DoctorLeaves { get; }
        DbSet<AuditLog> AuditLogs { get; }
        DbSet<MedicalRecord> MedicalRecords { get; }
        DbSet<SmsMessage> SmsMessages { get; }
        DbSet<LabTest> LabTests { get; }
        DbSet<ExaminationTypeLabTest> ExaminationTypeLabTests { get; }
        DbSet<LabOrder> LabOrders { get; }
        DbSet<LabResult> LabResults { get; }
        DbSet<StockItem> StockItems { get; }
        DbSet<Warehouse> Warehouses { get; }
        DbSet<Supplier> Suppliers { get; }
        DbSet<StockBatch> StockBatches { get; }
        DbSet<StockDocument> StockDocuments { get; }
        DbSet<StockMovement> StockMovements { get; }
        DbSet<ExaminationTypeStockItem> ExaminationTypeStockItems { get; }
        DbSet<Ward> Wards { get; }
        DbSet<Bed> Beds { get; }
        DbSet<Admission> Admissions { get; }
        DbSet<BedStay> BedStays { get; }
        DbSet<Observation> Observations { get; }
        DbSet<MedicationOrder> MedicationOrders { get; }
        DbSet<MedicationDose> MedicationDoses { get; }

        DbSet<OutboxMessage> OutboxMessages { get; }
        DbSet<Equipment> Equipment { get; }
        DbSet<CancellationReason> CancellationReasons { get; }

        ChangeTracker ChangeTracker { get; }
        int SaveChanges();
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
