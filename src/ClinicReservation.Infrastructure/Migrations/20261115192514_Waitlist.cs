using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class Waitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    DoctorId = table.Column<int>(type: "integer", nullable: true),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    ExaminationTypeId = table.Column<int>(type: "integer", nullable: true),
                    FromDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ToDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TimeFrom = table.Column<TimeSpan>(type: "interval", nullable: true),
                    TimeTo = table.Column<TimeSpan>(type: "interval", nullable: true),
                    NotifyBySms = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    OfferedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OfferedSlot = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OfferedDoctorId = table.Column<int>(type: "integer", nullable: true),
                    ReserveId = table.Column<int>(type: "integer", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CloseNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "core",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalSchema: "core",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Doctors_OfferedDoctorId",
                        column: x => x.OfferedDoctorId,
                        principalSchema: "core",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_ExaminationTypes_ExaminationTypeId",
                        column: x => x.ExaminationTypeId,
                        principalSchema: "core",
                        principalTable: "ExaminationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Reserves_ReserveId",
                        column: x => x.ReserveId,
                        principalSchema: "scheduling",
                        principalTable: "Reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_DepartmentId",
                schema: "scheduling",
                table: "WaitlistEntries",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_DoctorId",
                schema: "scheduling",
                table: "WaitlistEntries",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_ExaminationTypeId",
                schema: "scheduling",
                table: "WaitlistEntries",
                column: "ExaminationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_OfferedDoctorId",
                schema: "scheduling",
                table: "WaitlistEntries",
                column: "OfferedDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_PatientId",
                schema: "scheduling",
                table: "WaitlistEntries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_ReserveId",
                schema: "scheduling",
                table: "WaitlistEntries",
                column: "ReserveId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_Status_FromDate",
                schema: "scheduling",
                table: "WaitlistEntries",
                columns: new[] { "Status", "FromDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WaitlistEntries",
                schema: "scheduling");
        }
    }
}
