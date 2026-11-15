using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class ReserveSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesNo",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReserveSeries",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    DoctorId = table.Column<int>(type: "integer", nullable: true),
                    Pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReserveSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReserveSeries_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reserves_SeriesId",
                schema: "scheduling",
                table: "Reserves",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_ReserveSeries_PatientId",
                schema: "scheduling",
                table: "ReserveSeries",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reserves_ReserveSeries_SeriesId",
                schema: "scheduling",
                table: "Reserves",
                column: "SeriesId",
                principalSchema: "scheduling",
                principalTable: "ReserveSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reserves_ReserveSeries_SeriesId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropTable(
                name: "ReserveSeries",
                schema: "scheduling");

            migrationBuilder.DropIndex(
                name: "IX_Reserves_SeriesId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "SeriesNo",
                schema: "scheduling",
                table: "Reserves");
        }
    }
}
