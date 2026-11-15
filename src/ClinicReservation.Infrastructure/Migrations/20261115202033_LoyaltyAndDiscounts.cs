using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyAndDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "loyalty");

            migrationBuilder.AddColumn<int>(
                name: "DiscountKind",
                schema: "scheduling",
                table: "ReserveExaminationTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiscountRefId",
                schema: "scheduling",
                table: "ReserveExaminationTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                schema: "billing",
                table: "Payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "BenefitCategories",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenefitCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StartsOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    MaxUsesPerPatient = table.Column<int>(type: "integer", nullable: true),
                    NewPatientsOnly = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Campaigns_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "core",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContractNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ContractFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ContractUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Contact = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyTiers",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MinSpend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PointsPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyTiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatientBenefits",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    DocumentNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AddedBy = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientBenefits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientBenefits_BenefitCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "loyalty",
                        principalTable: "BenefitCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientBenefits_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignExams",
                schema: "loyalty",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    ExaminationTypeId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignExams", x => new { x.CampaignId, x.ExaminationTypeId });
                    table.ForeignKey(
                        name: "FK_CampaignExams_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalSchema: "loyalty",
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignExams_ExaminationTypes_ExaminationTypeId",
                        column: x => x.ExaminationTypeId,
                        principalSchema: "core",
                        principalTable: "ExaminationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatientCompanies",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientCompanies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientCompanies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "loyalty",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCompanies_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyCards",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HolderPatientId = table.Column<int>(type: "integer", nullable: false),
                    IsFamily = table.Column<bool>(type: "boolean", nullable: false),
                    TierOverrideId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyCards_LoyaltyTiers_TierOverrideId",
                        column: x => x.TierOverrideId,
                        principalSchema: "loyalty",
                        principalTable: "LoyaltyTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LoyaltyCards_Patients_HolderPatientId",
                        column: x => x.HolderPatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyCardMembers",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CardId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyCardMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyCardMembers_LoyaltyCards_CardId",
                        column: x => x.CardId,
                        principalSchema: "loyalty",
                        principalTable: "LoyaltyCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoyaltyCardMembers_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "core",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyTransactions",
                schema: "loyalty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CardId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: true),
                    ReserveId = table.Column<int>(type: "integer", nullable: true),
                    PaymentKey = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Spend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    By = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_LoyaltyCards_CardId",
                        column: x => x.CardId,
                        principalSchema: "loyalty",
                        principalTable: "LoyaltyCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignExams_ExaminationTypeId",
                schema: "loyalty",
                table: "CampaignExams",
                column: "ExaminationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Code",
                schema: "loyalty",
                table: "Campaigns",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_DepartmentId",
                schema: "loyalty",
                table: "Campaigns",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyCardMembers_CardId",
                schema: "loyalty",
                table: "LoyaltyCardMembers",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyCardMembers_PatientId",
                schema: "loyalty",
                table: "LoyaltyCardMembers",
                column: "PatientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyCards_HolderPatientId",
                schema: "loyalty",
                table: "LoyaltyCards",
                column: "HolderPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyCards_Number",
                schema: "loyalty",
                table: "LoyaltyCards",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyCards_TierOverrideId",
                schema: "loyalty",
                table: "LoyaltyCards",
                column: "TierOverrideId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_CardId_At",
                schema: "loyalty",
                table: "LoyaltyTransactions",
                columns: new[] { "CardId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_PaymentKey",
                schema: "loyalty",
                table: "LoyaltyTransactions",
                column: "PaymentKey",
                unique: true,
                filter: "\"PaymentKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientBenefits_CategoryId",
                schema: "loyalty",
                table: "PatientBenefits",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientBenefits_PatientId",
                schema: "loyalty",
                table: "PatientBenefits",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCompanies_CompanyId",
                schema: "loyalty",
                table: "PatientCompanies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCompanies_PatientId",
                schema: "loyalty",
                table: "PatientCompanies",
                column: "PatientId",
                unique: true);

            // Every existing payment gets its own key; follow-up discounts from before get their kind.
            migrationBuilder.Sql(@"UPDATE billing.""Payments"" SET ""Key"" = gen_random_uuid();");
            migrationBuilder.Sql(@"UPDATE scheduling.""ReserveExaminationTypes"" SET ""DiscountKind"" = 1 WHERE ""DiscountSource"" LIKE 'Kontrol qəbulu%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignExams",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "LoyaltyCardMembers",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "LoyaltyTransactions",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "PatientBenefits",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "PatientCompanies",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "Campaigns",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "LoyaltyCards",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "BenefitCategories",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "LoyaltyTiers",
                schema: "loyalty");

            migrationBuilder.DropColumn(
                name: "DiscountKind",
                schema: "scheduling",
                table: "ReserveExaminationTypes");

            migrationBuilder.DropColumn(
                name: "DiscountRefId",
                schema: "scheduling",
                table: "ReserveExaminationTypes");

            migrationBuilder.DropColumn(
                name: "Key",
                schema: "billing",
                table: "Payments");
        }
    }
}
