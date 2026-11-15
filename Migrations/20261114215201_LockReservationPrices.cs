using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class LockReservationPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "ReserveExaminationTypes",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Existing reservations: lock in the examination's current price.
            migrationBuilder.Sql(@"
                UPDATE ""ReserveExaminationTypes"" AS r
                SET ""Price"" = e.""Price""
                FROM ""ExaminationTypes"" AS e
                WHERE r.""ExaminationTypeId"" = e.""Id"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Price",
                table: "ReserveExaminationTypes");
        }
    }
}
