using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class LaboratoryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LabOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReserveId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    Barcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CollectedBy = table.Column<string>(type: "text", nullable: true),
                    ValidatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ValidatedBy = table.Column<string>(type: "text", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabOrders_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabOrders_Reserves_ReserveId",
                        column: x => x.ReserveId,
                        principalTable: "Reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LabTests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SampleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ResultType = table.Column<int>(type: "integer", nullable: false),
                    Choices = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RefMin = table.Column<decimal>(type: "numeric", nullable: true),
                    RefMax = table.Column<decimal>(type: "numeric", nullable: true),
                    RefMinFemale = table.Column<decimal>(type: "numeric", nullable: true),
                    RefMaxFemale = table.Column<decimal>(type: "numeric", nullable: true),
                    RefText = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CriticalMin = table.Column<decimal>(type: "numeric", nullable: true),
                    CriticalMax = table.Column<decimal>(type: "numeric", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabTests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExaminationTypeLabTests",
                columns: table => new
                {
                    ExaminationTypeId = table.Column<int>(type: "integer", nullable: false),
                    LabTestId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExaminationTypeLabTests", x => new { x.ExaminationTypeId, x.LabTestId });
                    table.ForeignKey(
                        name: "FK_ExaminationTypeLabTests_ExaminationTypes_ExaminationTypeId",
                        column: x => x.ExaminationTypeId,
                        principalTable: "ExaminationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExaminationTypeLabTests_LabTests_LabTestId",
                        column: x => x.LabTestId,
                        principalTable: "LabTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LabResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LabOrderId = table.Column<int>(type: "integer", nullable: false),
                    LabTestId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NumericValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Flag = table.Column<int>(type: "integer", nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EnteredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EnteredBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabResults_LabOrders_LabOrderId",
                        column: x => x.LabOrderId,
                        principalTable: "LabOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LabResults_LabTests_LabTestId",
                        column: x => x.LabTestId,
                        principalTable: "LabTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTypeLabTests_LabTestId",
                table: "ExaminationTypeLabTests",
                column: "LabTestId");

            migrationBuilder.CreateIndex(
                name: "IX_LabOrders_Barcode",
                table: "LabOrders",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabOrders_CollectedAt",
                table: "LabOrders",
                column: "CollectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LabOrders_PatientId",
                table: "LabOrders",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_LabOrders_ReserveId",
                table: "LabOrders",
                column: "ReserveId");

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_LabOrderId_LabTestId",
                table: "LabResults",
                columns: new[] { "LabOrderId", "LabTestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_LabTestId",
                table: "LabResults",
                column: "LabTestId");

            migrationBuilder.CreateIndex(
                name: "IX_LabTests_Code",
                table: "LabTests",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExaminationTypeLabTests");

            migrationBuilder.DropTable(
                name: "LabResults");

            migrationBuilder.DropTable(
                name: "LabOrders");

            migrationBuilder.DropTable(
                name: "LabTests");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Patients");
        }
    }
}
