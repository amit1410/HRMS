using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceDeviceSyncRecoveryPhase6G : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "AttendanceDeviceSyncRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "AttendanceDeviceSyncRuns",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureMessage",
                table: "AttendanceDeviceSyncRuns",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailureCount",
                table: "AttendanceDevices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailureAtUtc",
                table: "AttendanceDevices",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFailureCode",
                table: "AttendanceDevices",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                table: "AttendanceDevices",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "AttendanceDeviceSyncRuns");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "AttendanceDeviceSyncRuns");

            migrationBuilder.DropColumn(
                name: "FailureMessage",
                table: "AttendanceDeviceSyncRuns");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailureCount",
                table: "AttendanceDevices");

            migrationBuilder.DropColumn(
                name: "LastFailureAtUtc",
                table: "AttendanceDevices");

            migrationBuilder.DropColumn(
                name: "LastFailureCode",
                table: "AttendanceDevices");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                table: "AttendanceDevices");
        }
    }
}
