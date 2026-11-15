using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class QueueDesksAndPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                schema: "scheduling",
                table: "Reserves",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ServiceWindows",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceWindows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeskTickets",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Ticket = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IssuedBy = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    WindowId = table.Column<int>(type: "integer", nullable: true),
                    CalledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ServedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ServedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeskTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeskTickets_ServiceWindows_WindowId",
                        column: x => x.WindowId,
                        principalSchema: "scheduling",
                        principalTable: "ServiceWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeskTickets_Kind_IssuedAt",
                schema: "scheduling",
                table: "DeskTickets",
                columns: new[] { "Kind", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeskTickets_WindowId",
                schema: "scheduling",
                table: "DeskTickets",
                column: "WindowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeskTickets",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "ServiceWindows",
                schema: "scheduling");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "scheduling",
                table: "Reserves");
        }
    }
}
