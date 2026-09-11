using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRosterHistoryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChangeType",
                table: "EmployeeRosterChangeHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAtUtc",
                table: "EmployeeRosterChangeHistories",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedByUserId",
                table: "EmployeeRosterChangeHistories",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NewIsCalendarOverride",
                table: "EmployeeRosterChangeHistories",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OriginalCalendarDayType",
                table: "EmployeeRosterChangeHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PreviousIsCalendarOverride",
                table: "EmployeeRosterChangeHistories",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeType",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.DropColumn(
                name: "ChangedAtUtc",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.DropColumn(
                name: "ChangedByUserId",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.DropColumn(
                name: "NewIsCalendarOverride",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.DropColumn(
                name: "OriginalCalendarDayType",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.DropColumn(
                name: "PreviousIsCalendarOverride",
                table: "EmployeeRosterChangeHistories");
        }
    }
}
