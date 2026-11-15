using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class NoShowMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NoShowAt",
                schema: "scheduling",
                table: "Reserves",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NoShowAuto",
                schema: "scheduling",
                table: "Reserves",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoShowAt",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "NoShowAuto",
                schema: "scheduling",
                table: "Reserves");
        }
    }
}
