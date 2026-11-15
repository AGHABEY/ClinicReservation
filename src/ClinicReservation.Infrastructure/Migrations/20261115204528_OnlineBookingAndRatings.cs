using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class OnlineBookingAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RescheduleCount",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "OnlineBookable",
                schema: "core",
                table: "ExaminationTypes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlineBooking",
                schema: "core",
                table: "Doctors",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "OtpCodes",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    Payload = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitRatings",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReserveId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    DoctorId = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitRatings_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalSchema: "core",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VisitRatings_Reserves_ReserveId",
                        column: x => x.ReserveId,
                        principalSchema: "scheduling",
                        principalTable: "Reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_Key",
                schema: "scheduling",
                table: "OtpCodes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_Phone_CreatedAt",
                schema: "scheduling",
                table: "OtpCodes",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitRatings_DoctorId_CreatedAt",
                schema: "scheduling",
                table: "VisitRatings",
                columns: new[] { "DoctorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitRatings_ReserveId",
                schema: "scheduling",
                table: "VisitRatings",
                column: "ReserveId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OtpCodes",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "VisitRatings",
                schema: "scheduling");

            migrationBuilder.DropColumn(
                name: "RescheduleCount",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "OnlineBookable",
                schema: "core",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "OnlineBooking",
                schema: "core",
                table: "Doctors");
        }
    }
}
