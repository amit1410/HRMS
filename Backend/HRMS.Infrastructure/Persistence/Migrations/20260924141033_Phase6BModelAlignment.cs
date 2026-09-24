using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6BModelAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AttendanceEligibleDays",
                table: "PayrollResults",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AttendanceLopDays",
                table: "PayrollResults",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AttendancePayableDays",
                table: "PayrollResults",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AttendanceSnapshotId",
                table: "PayrollResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttendanceVersion",
                table: "PayrollResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LopDays",
                table: "EmployeeAttendanceMonthlySummaries",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidLeaveDays",
                table: "EmployeeAttendanceMonthlySummaries",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PayableDays",
                table: "EmployeeAttendanceMonthlySummaries",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PresentDayQuantity",
                table: "EmployeeAttendanceMonthlySummaries",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidLeaveDays",
                table: "EmployeeAttendanceMonthlySummaries",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "EmployeeAttendanceMonthlySummaries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PayrollAttendanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmploymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    EligibleDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PayableDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LopDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PresentDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AbsentDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PaidLeaveDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnpaidLeaveDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HolidayDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WeekOffDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OnDutyDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAttendanceSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_PayrollAttendanceSnapshots_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollAttendanceSnapshots_AttendancePeriods_TenantId_AttendancePeriodId",
                        columns: x => new { x.TenantId, x.AttendancePeriodId },
                        principalTable: "AttendancePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAttendanceSnapshots_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAttendanceSnapshots_TenantId_AttendancePeriodId_EmployeeId_IsCurrent",
                table: "PayrollAttendanceSnapshots",
                columns: new[] { "TenantId", "AttendancePeriodId", "EmployeeId", "IsCurrent" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAttendanceSnapshots_TenantId_AttendancePeriodId_EmployeeId_Version",
                table: "PayrollAttendanceSnapshots",
                columns: new[] { "TenantId", "AttendancePeriodId", "EmployeeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAttendanceSnapshots_TenantId_EmployeeId",
                table: "PayrollAttendanceSnapshots",
                columns: new[] { "TenantId", "EmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollAttendanceSnapshots");

            migrationBuilder.DropColumn(
                name: "AttendanceEligibleDays",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "AttendanceLopDays",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "AttendancePayableDays",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "AttendanceSnapshotId",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "AttendanceVersion",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "LopDays",
                table: "EmployeeAttendanceMonthlySummaries");

            migrationBuilder.DropColumn(
                name: "PaidLeaveDays",
                table: "EmployeeAttendanceMonthlySummaries");

            migrationBuilder.DropColumn(
                name: "PayableDays",
                table: "EmployeeAttendanceMonthlySummaries");

            migrationBuilder.DropColumn(
                name: "PresentDayQuantity",
                table: "EmployeeAttendanceMonthlySummaries");

            migrationBuilder.DropColumn(
                name: "UnpaidLeaveDays",
                table: "EmployeeAttendanceMonthlySummaries");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "EmployeeAttendanceMonthlySummaries");
        }
    }
}
