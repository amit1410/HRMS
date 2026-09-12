using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceMonthlyFinalizationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendancePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DataVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastProcessedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ProcessingRunId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePeriods", x => x.Id);
                    table.UniqueConstraint("AK_AttendancePeriods_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendancePeriods_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendancePeriods_Users_TenantId_CreatedByUserId",
                        columns: x => new { x.TenantId, x.CreatedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendancePeriods_Users_TenantId_LastProcessedByUserId",
                        columns: x => new { x.TenantId, x.LastProcessedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendancePeriodEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DataVersion = table.Column<int>(type: "int", nullable: false),
                    Details = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePeriodEvents", x => x.Id);
                    table.UniqueConstraint("AK_AttendancePeriodEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendancePeriodEvents_AttendancePeriods_TenantId_Attendance~",
                        columns: x => new { x.TenantId, x.AttendancePeriodId },
                        principalTable: "AttendancePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendancePeriodEvents_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeAttendanceMonthlySummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    EmployeeName = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    CalendarDays = table.Column<int>(type: "int", nullable: false),
                    EmploymentDays = table.Column<int>(type: "int", nullable: false),
                    WorkingDays = table.Column<int>(type: "int", nullable: false),
                    PresentDays = table.Column<int>(type: "int", nullable: false),
                    AbsentDays = table.Column<int>(type: "int", nullable: false),
                    OnLeaveDays = table.Column<int>(type: "int", nullable: false),
                    OnDutyDays = table.Column<int>(type: "int", nullable: false),
                    HolidayDays = table.Column<int>(type: "int", nullable: false),
                    WeeklyOffDays = table.Column<int>(type: "int", nullable: false),
                    IncompleteDays = table.Column<int>(type: "int", nullable: false),
                    NotProcessedDays = table.Column<int>(type: "int", nullable: false),
                    LateInCount = table.Column<int>(type: "int", nullable: false),
                    EarlyOutCount = table.Column<int>(type: "int", nullable: false),
                    GraceAppliedCount = table.Column<int>(type: "int", nullable: false),
                    MissingInCount = table.Column<int>(type: "int", nullable: false),
                    MissingOutCount = table.Column<int>(type: "int", nullable: false),
                    RegularizedDays = table.Column<int>(type: "int", nullable: false),
                    ApprovedOnDutyDays = table.Column<int>(type: "int", nullable: false),
                    LeaveConflictCount = table.Column<int>(type: "int", nullable: false),
                    ExceptionCount = table.Column<int>(type: "int", nullable: false),
                    ExpectedWorkMinutes = table.Column<int>(type: "int", nullable: false),
                    ActualWorkMinutes = table.Column<int>(type: "int", nullable: false),
                    SourceDataVersion = table.Column<int>(type: "int", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAttendanceMonthlySummaries", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeAttendanceMonthlySummaries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeAttendanceMonthlySummaries_AttendancePeriods_TenantI~",
                        columns: x => new { x.TenantId, x.AttendancePeriodId },
                        principalTable: "AttendancePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAttendanceMonthlySummaries_Employees_TenantId_Employ~",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriodEvents_TenantId_ActorUserId",
                table: "AttendancePeriodEvents",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriodEvents_TenantId_AttendancePeriodId_OccurredA~",
                table: "AttendancePeriodEvents",
                columns: new[] { "TenantId", "AttendancePeriodId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriods_TenantId_CreatedByUserId",
                table: "AttendancePeriods",
                columns: new[] { "TenantId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriods_TenantId_LastProcessedByUserId",
                table: "AttendancePeriods",
                columns: new[] { "TenantId", "LastProcessedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriods_TenantId_Status",
                table: "AttendancePeriods",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriods_TenantId_Year_Month",
                table: "AttendancePeriods",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendanceMonthlySummaries_TenantId_AttendancePerio~1",
                table: "EmployeeAttendanceMonthlySummaries",
                columns: new[] { "TenantId", "AttendancePeriodId", "ExceptionCount" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendanceMonthlySummaries_TenantId_AttendancePeriod~",
                table: "EmployeeAttendanceMonthlySummaries",
                columns: new[] { "TenantId", "AttendancePeriodId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendanceMonthlySummaries_TenantId_EmployeeId",
                table: "EmployeeAttendanceMonthlySummaries",
                columns: new[] { "TenantId", "EmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendancePeriodEvents");

            migrationBuilder.DropTable(
                name: "EmployeeAttendanceMonthlySummaries");

            migrationBuilder.DropTable(
                name: "AttendancePeriods");

        }
    }
}
