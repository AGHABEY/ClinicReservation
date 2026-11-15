using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class ModuleSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inpatient");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "lab");

            migrationBuilder.EnsureSchema(
                name: "stock");

            migrationBuilder.EnsureSchema(
                name: "emr");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.EnsureSchema(
                name: "notify");

            migrationBuilder.RenameTable(
                name: "Warehouses",
                newName: "Warehouses",
                newSchema: "stock");

            migrationBuilder.RenameTable(
                name: "Wards",
                newName: "Wards",
                newSchema: "inpatient");

            migrationBuilder.RenameTable(
                name: "Suppliers",
                newName: "Suppliers",
                newSchema: "stock");

            migrationBuilder.RenameTable(
                name: "StockMovements",
                newName: "StockMovements",
                newSchema: "stock");

            migrationBuilder.RenameTable(
                name: "StockItems",
                newName: "StockItems",
                newSchema: "stock");

            migrationBuilder.RenameTable(
                name: "StockDocuments",
                newName: "StockDocuments",
                newSchema: "stock");

            migrationBuilder.RenameTable(
                name: "StockBatches",
                newName: "StockBatches",
                newSchema: "stock");

            migrationBuilder.RenameTable(
                name: "SmsMessages",
                newName: "SmsMessages",
                newSchema: "notify");

            migrationBuilder.RenameTable(
                name: "Rooms",
                newName: "Rooms",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "ReserveStatuses",
                newName: "ReserveStatuses",
                newSchema: "scheduling");

            migrationBuilder.RenameTable(
                name: "Reserves",
                newName: "Reserves",
                newSchema: "scheduling");

            migrationBuilder.RenameTable(
                name: "ReserveExaminationTypes",
                newName: "ReserveExaminationTypes",
                newSchema: "scheduling");

            migrationBuilder.RenameTable(
                name: "Payments",
                newName: "Payments",
                newSchema: "billing");

            migrationBuilder.RenameTable(
                name: "Patients",
                newName: "Patients",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "OutboxMessages",
                newName: "OutboxMessages",
                newSchema: "platform");

            migrationBuilder.RenameTable(
                name: "Observations",
                newName: "Observations",
                newSchema: "inpatient");

            migrationBuilder.RenameTable(
                name: "MedicationOrders",
                newName: "MedicationOrders",
                newSchema: "inpatient");

            migrationBuilder.RenameTable(
                name: "MedicationDoses",
                newName: "MedicationDoses",
                newSchema: "inpatient");

            migrationBuilder.RenameTable(
                name: "MedicalRecords",
                newName: "MedicalRecords",
                newSchema: "emr");

            migrationBuilder.RenameTable(
                name: "LabTests",
                newName: "LabTests",
                newSchema: "lab");

            migrationBuilder.RenameTable(
                name: "LabResults",
                newName: "LabResults",
                newSchema: "lab");

            migrationBuilder.RenameTable(
                name: "LabOrders",
                newName: "LabOrders",
                newSchema: "lab");

            migrationBuilder.RenameTable(
                name: "ExaminationTypeStockItems",
                newName: "ExaminationTypeStockItems",
                newSchema: "stock");

            migrationBuilder.RenameTable(
                name: "ExaminationTypes",
                newName: "ExaminationTypes",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "ExaminationTypeLabTests",
                newName: "ExaminationTypeLabTests",
                newSchema: "lab");

            migrationBuilder.RenameTable(
                name: "DoctorWorkHours",
                newName: "DoctorWorkHours",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "Doctors",
                newName: "Doctors",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "DoctorLeaves",
                newName: "DoctorLeaves",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "Departments",
                newName: "Departments",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "BedStays",
                newName: "BedStays",
                newSchema: "inpatient");

            migrationBuilder.RenameTable(
                name: "Beds",
                newName: "Beds",
                newSchema: "inpatient");

            migrationBuilder.RenameTable(
                name: "AuditLogs",
                newName: "AuditLogs",
                newSchema: "audit");

            migrationBuilder.RenameTable(
                name: "Admissions",
                newName: "Admissions",
                newSchema: "inpatient");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Warehouses",
                schema: "stock",
                newName: "Warehouses");

            migrationBuilder.RenameTable(
                name: "Wards",
                schema: "inpatient",
                newName: "Wards");

            migrationBuilder.RenameTable(
                name: "Suppliers",
                schema: "stock",
                newName: "Suppliers");

            migrationBuilder.RenameTable(
                name: "StockMovements",
                schema: "stock",
                newName: "StockMovements");

            migrationBuilder.RenameTable(
                name: "StockItems",
                schema: "stock",
                newName: "StockItems");

            migrationBuilder.RenameTable(
                name: "StockDocuments",
                schema: "stock",
                newName: "StockDocuments");

            migrationBuilder.RenameTable(
                name: "StockBatches",
                schema: "stock",
                newName: "StockBatches");

            migrationBuilder.RenameTable(
                name: "SmsMessages",
                schema: "notify",
                newName: "SmsMessages");

            migrationBuilder.RenameTable(
                name: "Rooms",
                schema: "core",
                newName: "Rooms");

            migrationBuilder.RenameTable(
                name: "ReserveStatuses",
                schema: "scheduling",
                newName: "ReserveStatuses");

            migrationBuilder.RenameTable(
                name: "Reserves",
                schema: "scheduling",
                newName: "Reserves");

            migrationBuilder.RenameTable(
                name: "ReserveExaminationTypes",
                schema: "scheduling",
                newName: "ReserveExaminationTypes");

            migrationBuilder.RenameTable(
                name: "Payments",
                schema: "billing",
                newName: "Payments");

            migrationBuilder.RenameTable(
                name: "Patients",
                schema: "core",
                newName: "Patients");

            migrationBuilder.RenameTable(
                name: "OutboxMessages",
                schema: "platform",
                newName: "OutboxMessages");

            migrationBuilder.RenameTable(
                name: "Observations",
                schema: "inpatient",
                newName: "Observations");

            migrationBuilder.RenameTable(
                name: "MedicationOrders",
                schema: "inpatient",
                newName: "MedicationOrders");

            migrationBuilder.RenameTable(
                name: "MedicationDoses",
                schema: "inpatient",
                newName: "MedicationDoses");

            migrationBuilder.RenameTable(
                name: "MedicalRecords",
                schema: "emr",
                newName: "MedicalRecords");

            migrationBuilder.RenameTable(
                name: "LabTests",
                schema: "lab",
                newName: "LabTests");

            migrationBuilder.RenameTable(
                name: "LabResults",
                schema: "lab",
                newName: "LabResults");

            migrationBuilder.RenameTable(
                name: "LabOrders",
                schema: "lab",
                newName: "LabOrders");

            migrationBuilder.RenameTable(
                name: "ExaminationTypeStockItems",
                schema: "stock",
                newName: "ExaminationTypeStockItems");

            migrationBuilder.RenameTable(
                name: "ExaminationTypes",
                schema: "core",
                newName: "ExaminationTypes");

            migrationBuilder.RenameTable(
                name: "ExaminationTypeLabTests",
                schema: "lab",
                newName: "ExaminationTypeLabTests");

            migrationBuilder.RenameTable(
                name: "DoctorWorkHours",
                schema: "core",
                newName: "DoctorWorkHours");

            migrationBuilder.RenameTable(
                name: "Doctors",
                schema: "core",
                newName: "Doctors");

            migrationBuilder.RenameTable(
                name: "DoctorLeaves",
                schema: "core",
                newName: "DoctorLeaves");

            migrationBuilder.RenameTable(
                name: "Departments",
                schema: "core",
                newName: "Departments");

            migrationBuilder.RenameTable(
                name: "BedStays",
                schema: "inpatient",
                newName: "BedStays");

            migrationBuilder.RenameTable(
                name: "Beds",
                schema: "inpatient",
                newName: "Beds");

            migrationBuilder.RenameTable(
                name: "AuditLogs",
                schema: "audit",
                newName: "AuditLogs");

            migrationBuilder.RenameTable(
                name: "Admissions",
                schema: "inpatient",
                newName: "Admissions");
        }
    }
}
