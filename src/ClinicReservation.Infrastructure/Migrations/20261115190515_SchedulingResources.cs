using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class SchedulingResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CancellationReasonId",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledBy",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquipmentId",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                schema: "core",
                table: "ExaminationTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquipmentId",
                schema: "core",
                table: "ExaminationTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CancellationReasons",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Party = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Equipment",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RoomId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipment_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalSchema: "core",
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reserves_CancellationReasonId",
                schema: "scheduling",
                table: "Reserves",
                column: "CancellationReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Reserves_EquipmentId_AppointmentAt",
                schema: "scheduling",
                table: "Reserves",
                columns: new[] { "EquipmentId", "AppointmentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTypes_EquipmentId",
                schema: "core",
                table: "ExaminationTypes",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_RoomId",
                schema: "core",
                table: "Equipment",
                column: "RoomId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExaminationTypes_Equipment_EquipmentId",
                schema: "core",
                table: "ExaminationTypes",
                column: "EquipmentId",
                principalSchema: "core",
                principalTable: "Equipment",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reserves_CancellationReasons_CancellationReasonId",
                schema: "scheduling",
                table: "Reserves",
                column: "CancellationReasonId",
                principalSchema: "scheduling",
                principalTable: "CancellationReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reserves_Equipment_EquipmentId",
                schema: "scheduling",
                table: "Reserves",
                column: "EquipmentId",
                principalSchema: "core",
                principalTable: "Equipment",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExaminationTypes_Equipment_EquipmentId",
                schema: "core",
                table: "ExaminationTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_Reserves_CancellationReasons_CancellationReasonId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropForeignKey(
                name: "FK_Reserves_Equipment_EquipmentId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropTable(
                name: "CancellationReasons",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "Equipment",
                schema: "core");

            migrationBuilder.DropIndex(
                name: "IX_Reserves_CancellationReasonId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropIndex(
                name: "IX_Reserves_EquipmentId_AppointmentAt",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropIndex(
                name: "IX_ExaminationTypes_EquipmentId",
                schema: "core",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "CancellationReasonId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "EquipmentId",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "scheduling",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                schema: "core",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "EquipmentId",
                schema: "core",
                table: "ExaminationTypes");
        }
    }
}
