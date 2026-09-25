using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeCorrectionAuditPhase6C : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OvertimeVersion",
                table: "PayrollResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollOvertimeSnapshotId",
                table: "PayrollResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeMonthlyOvertimes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmploymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    NormalDayMinutes = table.Column<int>(type: "int", nullable: false),
                    WeekOffMinutes = table.Column<int>(type: "int", nullable: false),
                    HolidayMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalApprovedMinutes = table.Column<int>(type: "int", nullable: false),
                    AttendanceSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceVersion = table.Column<int>(type: "int", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    IsFinalized = table.Column<bool>(type: "bit", nullable: false),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    TenantId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeMonthlyOvertimes", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeMonthlyOvertimes_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeMonthlyOvertimes_AttendancePeriods_AttendancePeriodId",
                        column: x => x.AttendancePeriodId,
                        principalTable: "AttendancePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeMonthlyOvertimes_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeMonthlyOvertimes_PayrollAttendanceSnapshots_TenantId_AttendanceSnapshotId",
                        columns: x => new { x.TenantId, x.AttendanceSnapshotId },
                        principalTable: "PayrollAttendanceSnapshots",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeMonthlyOvertimes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeMonthlyOvertimes_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OvertimePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    MinimumExtraMinutes = table.Column<int>(type: "int", nullable: false),
                    RoundingMinutes = table.Column<int>(type: "int", nullable: false),
                    RoundingMode = table.Column<int>(type: "int", nullable: false),
                    MaximumMinutesPerDay = table.Column<int>(type: "int", nullable: true),
                    MaximumMinutesPerMonth = table.Column<int>(type: "int", nullable: true),
                    RequirePreApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequirePostApproval = table.Column<bool>(type: "bit", nullable: false),
                    AllowNormalWorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    AllowWeekOff = table.Column<bool>(type: "bit", nullable: false),
                    AllowHoliday = table.Column<bool>(type: "bit", nullable: false),
                    NormalDayMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    WeekOffMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    HolidayMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    EligibilityMode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HourlyRateComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MonthlyWorkMinutes = table.Column<int>(type: "int", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    TenantId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimePolicies", x => x.Id);
                    table.UniqueConstraint("AK_OvertimePolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_OvertimePolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimePolicies_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PayrollOvertimeSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmploymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    AttendanceSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceVersion = table.Column<int>(type: "int", nullable: false),
                    NormalDayMinutes = table.Column<int>(type: "int", nullable: false),
                    WeekOffMinutes = table.Column<int>(type: "int", nullable: false),
                    HolidayMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalApprovedMinutes = table.Column<int>(type: "int", nullable: false),
                    NormalDayMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    WeekOffMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    HolidayMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HourlyRateComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MonthlyWorkMinutes = table.Column<int>(type: "int", nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReopenReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollOvertimeSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_PayrollOvertimeSnapshots_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeSnapshots_AttendancePeriods_TenantId_AttendancePeriodId",
                        columns: x => new { x.TenantId, x.AttendancePeriodId },
                        principalTable: "AttendancePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeSnapshots_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeSnapshots_PayrollAttendanceSnapshots_TenantId_AttendanceSnapshotId",
                        columns: x => new { x.TenantId, x.AttendanceSnapshotId },
                        principalTable: "PayrollAttendanceSnapshots",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeSnapshots_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollOvertimeSnapshots_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OvertimeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmploymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedMinutes = table.Column<int>(type: "int", nullable: false),
                    ActualEligibleMinutes = table.Column<int>(type: "int", nullable: false),
                    ApprovedMinutes = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CorrectionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeRequests", x => x.Id);
                    table.UniqueConstraint("AK_OvertimeRequests_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_OvertimeRequests_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeRequests_OvertimePolicies_TenantId_PolicyId",
                        columns: x => new { x.TenantId, x.PolicyId },
                        principalTable: "OvertimePolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeRequests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeRequests_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeMonthlyOvertimes_AttendancePeriodId",
                table: "EmployeeMonthlyOvertimes",
                column: "AttendancePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeMonthlyOvertimes_TenantId_AttendancePeriodId_EmployeeId_IsCurrent",
                table: "EmployeeMonthlyOvertimes",
                columns: new[] { "TenantId", "AttendancePeriodId", "EmployeeId", "IsCurrent" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeMonthlyOvertimes_TenantId_AttendancePeriodId_EmployeeId_Version",
                table: "EmployeeMonthlyOvertimes",
                columns: new[] { "TenantId", "AttendancePeriodId", "EmployeeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeMonthlyOvertimes_TenantId_AttendanceSnapshotId",
                table: "EmployeeMonthlyOvertimes",
                columns: new[] { "TenantId", "AttendanceSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeMonthlyOvertimes_TenantId_EmployeeId",
                table: "EmployeeMonthlyOvertimes",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeMonthlyOvertimes_TenantId1",
                table: "EmployeeMonthlyOvertimes",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimePolicies_TenantId_Code",
                table: "OvertimePolicies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OvertimePolicies_TenantId_EffectiveFrom_EffectiveTo",
                table: "OvertimePolicies",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimePolicies_TenantId1",
                table: "OvertimePolicies",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequests_TenantId_EmployeeId_WorkDate_Status",
                table: "OvertimeRequests",
                columns: new[] { "TenantId", "EmployeeId", "WorkDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequests_TenantId_PolicyId",
                table: "OvertimeRequests",
                columns: new[] { "TenantId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequests_TenantId1",
                table: "OvertimeRequests",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollOvertimeSnapshots_TenantId_AttendancePeriodId_EmployeeId_IsCurrent",
                table: "PayrollOvertimeSnapshots",
                columns: new[] { "TenantId", "AttendancePeriodId", "EmployeeId", "IsCurrent" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollOvertimeSnapshots_TenantId_AttendancePeriodId_EmployeeId_Version",
                table: "PayrollOvertimeSnapshots",
                columns: new[] { "TenantId", "AttendancePeriodId", "EmployeeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollOvertimeSnapshots_TenantId_AttendanceSnapshotId",
                table: "PayrollOvertimeSnapshots",
                columns: new[] { "TenantId", "AttendanceSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollOvertimeSnapshots_TenantId_EmployeeId",
                table: "PayrollOvertimeSnapshots",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollOvertimeSnapshots_TenantId1",
                table: "PayrollOvertimeSnapshots",
                column: "TenantId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeMonthlyOvertimes");

            migrationBuilder.DropTable(
                name: "OvertimeRequests");

            migrationBuilder.DropTable(
                name: "PayrollOvertimeSnapshots");

            migrationBuilder.DropTable(
                name: "OvertimePolicies");

            migrationBuilder.DropColumn(
                name: "OvertimeVersion",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "PayrollOvertimeSnapshotId",
                table: "PayrollResults");
        }
    }
}
