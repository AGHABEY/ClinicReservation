using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicReservation.Migrations
{
    /// <inheritdoc />
    public partial class ClinicStructurePhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArrivedAt",
                table: "Reserves",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Reserves",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DoctorId",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Reserves",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "PatientId",
                table: "Reserves",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RoomId",
                table: "Reserves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "Reserves",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "ExaminationTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Surname = table.Column<string>(type: "text", nullable: false),
                    PinCode = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Birthday = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Doctors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Specialty = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    RoomId = table.Column<int>(type: "integer", nullable: true),
                    SlotMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Doctors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Doctors_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Doctors_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DoctorLeaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DoctorId = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    To = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorLeaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorLeaves_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DoctorWorkHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DoctorId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<TimeSpan>(type: "interval", nullable: false),
                    End = table.Column<TimeSpan>(type: "interval", nullable: false),
                    BreakStart = table.Column<TimeSpan>(type: "interval", nullable: true),
                    BreakEnd = table.Column<TimeSpan>(type: "interval", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorWorkHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorWorkHours_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- Data: one patient card per FİN, built from existing reservations ----
            // The newest reservation wins for name/phone; the card's CreatedAt is the first visit.
            migrationBuilder.Sql(@"
                INSERT INTO ""Patients"" (""Name"", ""Surname"", ""PinCode"", ""Phone"", ""Birthday"", ""CreatedAt"")
                SELECT DISTINCT ON (k.pin)
                       TRIM(r.""Name""), TRIM(r.""Surname""), k.pin, TRIM(r.""Phone""), r.""Birthday"",
                       (SELECT MIN(COALESCE(r2.""AddedDate"", NOW()::timestamp))
                          FROM ""Reserves"" r2
                         WHERE CASE WHEN COALESCE(TRIM(r2.""PinCode""), '') = '' THEN 'NOFIN-' || r2.""Id""
                                    ELSE UPPER(TRIM(r2.""PinCode"")) END = k.pin)
                  FROM ""Reserves"" r
                  CROSS JOIN LATERAL (SELECT CASE WHEN COALESCE(TRIM(r.""PinCode""), '') = '' THEN 'NOFIN-' || r.""Id""
                                                  ELSE UPPER(TRIM(r.""PinCode"")) END AS pin) k
                 ORDER BY k.pin, r.""Id"" DESC;");

            migrationBuilder.Sql(@"
                UPDATE ""Reserves"" r
                   SET ""PatientId"" = p.""Id""
                  FROM ""Patients"" p
                 WHERE p.""PinCode"" = CASE WHEN COALESCE(TRIM(r.""PinCode""), '') = '' THEN 'NOFIN-' || r.""Id""
                                            ELSE UPPER(TRIM(r.""PinCode"")) END;");

            migrationBuilder.CreateIndex(
                name: "IX_Reserves_DoctorId_AppointmentAt",
                table: "Reserves",
                columns: new[] { "DoctorId", "AppointmentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reserves_PatientId",
                table: "Reserves",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Reserves_RoomId",
                table: "Reserves",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTypes_DepartmentId",
                table: "ExaminationTypes",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At",
                table: "AuditLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorLeaves_DoctorId",
                table: "DoctorLeaves",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_DepartmentId",
                table: "Doctors",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_RoomId",
                table: "Doctors",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkHours_DoctorId_Day",
                table: "DoctorWorkHours",
                columns: new[] { "DoctorId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_PinCode",
                table: "Patients",
                column: "PinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Surname_Name",
                table: "Patients",
                columns: new[] { "Surname", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExaminationTypes_Departments_DepartmentId",
                table: "ExaminationTypes",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reserves_Doctors_DoctorId",
                table: "Reserves",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reserves_Patients_PatientId",
                table: "Reserves",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reserves_Rooms_RoomId",
                table: "Reserves",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Patient data now lives in Patients; drop the old per-reservation copies last.
            migrationBuilder.DropColumn(
                name: "Birthday",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "PinCode",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "Surname",
                table: "Reserves");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Birthday",
                table: "Reserves",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Reserves",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Reserves",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PinCode",
                table: "Reserves",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Surname",
                table: "Reserves",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE ""Reserves"" r
                   SET ""Name"" = p.""Name"", ""Surname"" = p.""Surname"", ""PinCode"" = p.""PinCode"",
                       ""Phone"" = p.""Phone"", ""Birthday"" = p.""Birthday""
                  FROM ""Patients"" p
                 WHERE p.""Id"" = r.""PatientId"";");

            migrationBuilder.DropForeignKey(
                name: "FK_ExaminationTypes_Departments_DepartmentId",
                table: "ExaminationTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_Reserves_Doctors_DoctorId",
                table: "Reserves");

            migrationBuilder.DropForeignKey(
                name: "FK_Reserves_Patients_PatientId",
                table: "Reserves");

            migrationBuilder.DropForeignKey(
                name: "FK_Reserves_Rooms_RoomId",
                table: "Reserves");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DoctorLeaves");

            migrationBuilder.DropTable(
                name: "DoctorWorkHours");

            migrationBuilder.DropTable(
                name: "Patients");

            migrationBuilder.DropTable(
                name: "Doctors");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Reserves_DoctorId_AppointmentAt",
                table: "Reserves");

            migrationBuilder.DropIndex(
                name: "IX_Reserves_PatientId",
                table: "Reserves");

            migrationBuilder.DropIndex(
                name: "IX_Reserves_RoomId",
                table: "Reserves");

            migrationBuilder.DropIndex(
                name: "IX_ExaminationTypes_DepartmentId",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "ArrivedAt",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "Reserves");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "ExaminationTypes");
        }
    }
}
