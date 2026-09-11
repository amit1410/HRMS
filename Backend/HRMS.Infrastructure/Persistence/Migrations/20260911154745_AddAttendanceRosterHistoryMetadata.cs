using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRosterHistoryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PreviousCalendarOverride",
                table: "EmployeeRosterChangeHistories",
                newName: "PreviousIsCalendarOverride");

            migrationBuilder.RenameColumn(
                name: "NewCalendarOverride",
                table: "EmployeeRosterChangeHistories",
                newName: "NewIsCalendarOverride");

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAtUtc",
                table: "EmployeeRosterChangeHistories",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedByUserId",
                table: "EmployeeRosterChangeHistories",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangedAtUtc",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.DropColumn(
                name: "ChangedByUserId",
                table: "EmployeeRosterChangeHistories");

            migrationBuilder.RenameColumn(
                name: "PreviousIsCalendarOverride",
                table: "EmployeeRosterChangeHistories",
                newName: "PreviousCalendarOverride");

            migrationBuilder.RenameColumn(
                name: "NewIsCalendarOverride",
                table: "EmployeeRosterChangeHistories",
                newName: "NewCalendarOverride");
        }
    }
}
