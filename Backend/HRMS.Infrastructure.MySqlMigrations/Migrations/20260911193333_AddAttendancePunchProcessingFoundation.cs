using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendancePunchProcessingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendancePunches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PunchAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    BusinessDate = table.Column<DateTime>(type: "date", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    ExternalPunchId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    DeviceId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RawReference = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePunches", x => x.Id);
                    table.UniqueConstraint("AK_AttendancePunches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendancePunches_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeAttendanceDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    BusinessDate = table.Column<DateTime>(type: "date", nullable: false),
                    ShiftId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ShiftCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    ScheduledStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ScheduledEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExpectedWorkMinutes = table.Column<int>(type: "int", nullable: true),
                    RosterAssignmentSource = table.Column<int>(type: "int", nullable: false),
                    RosterDayType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FirstPunchAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastPunchAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PunchCount = table.Column<int>(type: "int", nullable: false),
                    SessionCount = table.Column<int>(type: "int", nullable: false),
                    WorkedMinutes = table.Column<int>(type: "int", nullable: true),
                    BreakMinutes = table.Column<int>(type: "int", nullable: true),
                    IsLateIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsEarlyOut = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsGraceApplied = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsSinglePunch = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HasMissingInPunch = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HasMissingOutPunch = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LeaveConflict = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequiresMarkOutApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HasInvalidPunchSequence = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ProcessingOutcome = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAttendanceDays", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeAttendanceDays_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeAttendanceDays_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAttendanceDays_Shifts_TenantId_ShiftId",
                        columns: x => new { x.TenantId, x.ShiftId },
                        principalTable: "Shifts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunches_TenantId_EmployeeId_BusinessDate",
                table: "AttendancePunches",
                columns: new[] { "TenantId", "EmployeeId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunches_TenantId_EmployeeId_PunchAtUtc",
                table: "AttendancePunches",
                columns: new[] { "TenantId", "EmployeeId", "PunchAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunches_TenantId_Source_ExternalPunchId",
                table: "AttendancePunches",
                columns: new[] { "TenantId", "Source", "ExternalPunchId" },
                unique: true,
                filter: "ExternalPunchId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendanceDays_TenantId_BusinessDate",
                table: "EmployeeAttendanceDays",
                columns: new[] { "TenantId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendanceDays_TenantId_EmployeeId_BusinessDate",
                table: "EmployeeAttendanceDays",
                columns: new[] { "TenantId", "EmployeeId", "BusinessDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendanceDays_TenantId_ShiftId",
                table: "EmployeeAttendanceDays",
                columns: new[] { "TenantId", "ShiftId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendancePunches");

            migrationBuilder.DropTable(
                name: "EmployeeAttendanceDays");
        }
    }
}
