using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceAttendanceShiftConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowEarlyMarkIn",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowPresentOnSinglePunch",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AllowedAttendanceSources",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMarkOutMandatory",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "MandatoryEndTime",
                table: "Shifts",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "MandatoryStartTime",
                table: "Shifts",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaximumEarlyMarkInMinutes",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaximumPostShiftMinutes",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlannedDurationMinutes",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PostShiftMarkOutMode",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryAttendanceSource",
                table: "Shifts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireExpectedWorkMinutes",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ShiftType",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ShowEarlyOutIndicator",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowLateInIndicator",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StretchedEndTime",
                table: "Shifts",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StretchedStartTime",
                table: "Shifts",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseDefaultAttendanceMethodology",
                table: "Shifts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "ShiftApplicabilityRules",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalCalendarDayType",
                table: "EmployeeRosterDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NewSource",
                table: "EmployeeRosterChangeHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreviousSource",
                table: "EmployeeRosterChangeHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ShiftBreaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ShiftId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    IsPaid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftBreaks", x => x.Id);
                    table.UniqueConstraint("AK_ShiftBreaks_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ShiftBreaks_Shifts_TenantId_ShiftId",
                        columns: x => new { x.TenantId, x.ShiftId },
                        principalTable: "Shifts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_TenantId_IsDefault_EffectiveFrom",
                table: "Shifts",
                columns: new[] { "TenantId", "IsDefault", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftApplicabilityRules_TenantId_EmployeeId_EffectiveFrom",
                table: "ShiftApplicabilityRules",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftBreaks_TenantId_ShiftId_Sequence",
                table: "ShiftBreaks",
                columns: new[] { "TenantId", "ShiftId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftApplicabilityRules_Employees_TenantId_EmployeeId",
                table: "ShiftApplicabilityRules",
                columns: new[] { "TenantId", "EmployeeId" },
                principalTable: "Employees",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftApplicabilityRules_Employees_TenantId_EmployeeId",
                table: "ShiftApplicabilityRules");

            migrationBuilder.DropTable(
                name: "ShiftBreaks");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_TenantId_IsDefault_EffectiveFrom",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_ShiftApplicabilityRules_TenantId_EmployeeId_EffectiveFrom",
                table: "ShiftApplicabilityRules");

            migrationBuilder.DropColumn(
                name: "AllowEarlyMarkIn",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "AllowPresentOnSinglePunch",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "AllowedAttendanceSources",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "IsMarkOutMandatory",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "MandatoryEndTime",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "MandatoryStartTime",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "MaximumEarlyMarkInMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "MaximumPostShiftMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "PlannedDurationMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "PostShiftMarkOutMode",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "PrimaryAttendanceSource",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "RequireExpectedWorkMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ShiftType",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ShowEarlyOutIndicator",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ShowLateInIndicator",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "StretchedEndTime",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "StretchedStartTime",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "UseDefaultAttendanceMethodology",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "ShiftApplicabilityRules");

            migrationBuilder.DropColumn(
                name: "OriginalCalendarDayType",
                table: "EmployeeRosterDays");

            migrationBuilder.DropColumn(
                name: "NewSource",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.DropColumn(
                name: "PreviousSource",
                table: "EmployeeRosterChangeHistories");
        }
    }
}
