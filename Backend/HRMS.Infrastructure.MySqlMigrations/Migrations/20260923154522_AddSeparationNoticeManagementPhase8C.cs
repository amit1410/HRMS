using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationNoticeManagementPhase8C : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedNoticeEndDate",
                table: "EmployeeSeparations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastNoticeRevisionAtUtc",
                table: "EmployeeSeparations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticeDisposition",
                table: "EmployeeSeparations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NoticeExtensionDays",
                table: "EmployeeSeparations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WaivedNoticeDays",
                table: "EmployeeSeparations",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedNoticeEndDate",
                table: "EmployeeSeparations");

            migrationBuilder.DropColumn(
                name: "LastNoticeRevisionAtUtc",
                table: "EmployeeSeparations");

            migrationBuilder.DropColumn(
                name: "NoticeDisposition",
                table: "EmployeeSeparations");

            migrationBuilder.DropColumn(
                name: "NoticeExtensionDays",
                table: "EmployeeSeparations");

            migrationBuilder.DropColumn(
                name: "WaivedNoticeDays",
                table: "EmployeeSeparations");
        }
    }
}
