using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class CalendarOverbook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Overbooked",
                schema: "scheduling",
                table: "Reserves",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxOverbookPerDay",
                schema: "core",
                table: "Doctors",
                type: "integer",
                nullable: false,
                // Existing doctors get the same default as new ones (2 extra visits a day).
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Overbooked",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "MaxOverbookPerDay",
                schema: "core",
                table: "Doctors");
        }
    }
}
