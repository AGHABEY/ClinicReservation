using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppointmentAt",
                table: "Reserves",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelDate",
                table: "Reserves",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Reserves",
                type: "text",
                nullable: true);

            // Reservations created before appointments existed: use the time they were added.
            migrationBuilder.Sql(@"
                UPDATE ""Reserves""
                SET ""AppointmentAt"" = date_trunc('minute', COALESCE(""AddedDate"", NOW()::timestamp))
                WHERE ""AppointmentAt"" = '-infinity' OR ""AppointmentAt"" < '1900-01-01';");

            migrationBuilder.CreateIndex(
                name: "IX_Reserves_AppointmentAt",
                table: "Reserves",
                column: "AppointmentAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reserves_AppointmentAt",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "AppointmentAt",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "CancelDate",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Reserves");
        }
    }
}
