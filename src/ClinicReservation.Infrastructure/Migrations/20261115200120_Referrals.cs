using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class Referrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Referrals",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    FromReserveId = table.Column<int>(type: "integer", nullable: true),
                    FromDoctorId = table.Column<int>(type: "integer", nullable: true),
                    ExternalFrom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToDepartmentId = table.Column<int>(type: "integer", nullable: true),
                    ToDoctorId = table.Column<int>(type: "integer", nullable: true),
                    ExaminationTypeId = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IcdCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReserveId = table.Column<int>(type: "integer", nullable: true),
                    BookedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Referrals_Departments_ToDepartmentId",
                        column: x => x.ToDepartmentId,
                        principalSchema: "core",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Referrals_Doctors_FromDoctorId",
                        column: x => x.FromDoctorId,
                        principalSchema: "core",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Referrals_Doctors_ToDoctorId",
                        column: x => x.ToDoctorId,
                        principalSchema: "core",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Referrals_ExaminationTypes_ExaminationTypeId",
                        column: x => x.ExaminationTypeId,
                        principalSchema: "core",
                        principalTable: "ExaminationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Referrals_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_Reserves_FromReserveId",
                        column: x => x.FromReserveId,
                        principalSchema: "scheduling",
                        principalTable: "Reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Referrals_Reserves_ReserveId",
                        column: x => x.ReserveId,
                        principalSchema: "scheduling",
                        principalTable: "Reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ExaminationTypeId",
                schema: "scheduling",
                table: "Referrals",
                column: "ExaminationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_FromDoctorId",
                schema: "scheduling",
                table: "Referrals",
                column: "FromDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_FromReserveId",
                schema: "scheduling",
                table: "Referrals",
                column: "FromReserveId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_PatientId",
                schema: "scheduling",
                table: "Referrals",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReserveId",
                schema: "scheduling",
                table: "Referrals",
                column: "ReserveId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_Status_ValidUntil",
                schema: "scheduling",
                table: "Referrals",
                columns: new[] { "Status", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ToDepartmentId",
                schema: "scheduling",
                table: "Referrals",
                column: "ToDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ToDoctorId",
                schema: "scheduling",
                table: "Referrals",
                column: "ToDoctorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Referrals",
                schema: "scheduling");
        }
    }
}
