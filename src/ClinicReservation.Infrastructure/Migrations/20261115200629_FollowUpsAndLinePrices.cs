using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class FollowUpsAndLinePrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FollowUpOfId",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                schema: "scheduling",
                table: "ReserveExaminationTypes",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountSource",
                schema: "scheduling",
                table: "ReserveExaminationTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ListPrice",
                schema: "scheduling",
                table: "ReserveExaminationTypes",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "FollowUpDiscount",
                schema: "core",
                table: "ExaminationTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FollowUps",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromReserveId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    DoctorId = table.Column<int>(type: "integer", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SmsSentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReserveId = table.Column<int>(type: "integer", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CloseNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUps_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalSchema: "core",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FollowUps_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUps_Reserves_FromReserveId",
                        column: x => x.FromReserveId,
                        principalSchema: "scheduling",
                        principalTable: "Reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FollowUps_Reserves_ReserveId",
                        column: x => x.ReserveId,
                        principalSchema: "scheduling",
                        principalTable: "Reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_DoctorId",
                schema: "scheduling",
                table: "FollowUps",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_FromReserveId",
                schema: "scheduling",
                table: "FollowUps",
                column: "FromReserveId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_PatientId",
                schema: "scheduling",
                table: "FollowUps",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_ReserveId",
                schema: "scheduling",
                table: "FollowUps",
                column: "ReserveId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_Status_DueDate",
                schema: "scheduling",
                table: "FollowUps",
                columns: new[] { "Status", "DueDate" });

            // Existing lines were sold at their catalog price.
            migrationBuilder.Sql(@"UPDATE scheduling.""ReserveExaminationTypes"" SET ""ListPrice"" = ""Price"";");

            // Follow-ups the doctors already asked for (not long overdue, not seen again since) become plans.
            migrationBuilder.Sql(@"
INSERT INTO scheduling.""FollowUps"" (""FromReserveId"", ""PatientId"", ""DoctorId"", ""DueDate"", ""Status"", ""CreatedAt"")
SELECT m.""ReserveId"", m.""PatientId"", m.""DoctorId"", date_trunc('day', m.""CreatedAt"") + make_interval(days => m.""FollowUpDays""), 1, now()
FROM emr.""MedicalRecords"" m
WHERE m.""FollowUpDays"" IS NOT NULL
  AND date_trunc('day', m.""CreatedAt"") + make_interval(days => m.""FollowUpDays"") >= current_date - 30
  AND NOT EXISTS (SELECT 1 FROM scheduling.""Reserves"" r
                  WHERE r.""PatientId"" = m.""PatientId"" AND r.""DoctorId"" = m.""DoctorId"" AND r.""Id"" <> m.""ReserveId""
                    AND r.""AppointmentAt"" > m.""CreatedAt"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowUps",
                schema: "scheduling");

            migrationBuilder.DropColumn(
                name: "FollowUpOfId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "Discount",
                schema: "scheduling",
                table: "ReserveExaminationTypes");

            migrationBuilder.DropColumn(
                name: "DiscountSource",
                schema: "scheduling",
                table: "ReserveExaminationTypes");

            migrationBuilder.DropColumn(
                name: "ListPrice",
                schema: "scheduling",
                table: "ReserveExaminationTypes");

            migrationBuilder.DropColumn(
                name: "FollowUpDiscount",
                schema: "core",
                table: "ExaminationTypes");
        }
    }
}
