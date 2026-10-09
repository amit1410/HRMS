using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class DropEmploymentDateOfJoining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Date of joining becomes single-sourced from Employee (Personal Details). Before dropping
            // the duplicate EmployeeEmployments.DateOfJoining column we capture any divergence between
            // the employment mirror and the authoritative employee value into a standalone audit table,
            // so a conflict is reported for review rather than discarded silently. (A "missing employee
            // value" backfill is not applicable here: MySQL's DATE range cannot hold the DateOnly
            // default sentinel, and Employee.DateOfJoining is NOT NULL and always populated at creation.)
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `EmploymentDateOfJoiningMigrationConflicts` (
    `TenantId` char(36) NOT NULL,
    `EmployeeId` char(36) NOT NULL,
    `EmployeeDateOfJoining` date NOT NULL,
    `EmploymentDateOfJoining` date NOT NULL,
    `CapturedAtUtc` datetime(6) NOT NULL
);");

            migrationBuilder.Sql(@"
INSERT INTO `EmploymentDateOfJoiningMigrationConflicts`
    (`TenantId`, `EmployeeId`, `EmployeeDateOfJoining`, `EmploymentDateOfJoining`, `CapturedAtUtc`)
SELECT e.`TenantId`, e.`Id`, e.`DateOfJoining`, ee.`DateOfJoining`, UTC_TIMESTAMP(6)
FROM `Employees` e
INNER JOIN `EmployeeEmployments` ee ON ee.`EmployeeId` = e.`Id` AND ee.`TenantId` = e.`TenantId`
WHERE ee.`DateOfJoining` <> e.`DateOfJoining`;");

            migrationBuilder.DropColumn(
                name: "DateOfJoining",
                table: "EmployeeEmployments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfJoining",
                table: "EmployeeEmployments",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Re-hydrate the restored mirror column from the authoritative Employee value.
            migrationBuilder.Sql(@"
UPDATE `EmployeeEmployments` ee
INNER JOIN `Employees` e ON e.`Id` = ee.`EmployeeId` AND e.`TenantId` = ee.`TenantId`
SET ee.`DateOfJoining` = e.`DateOfJoining`;");

            migrationBuilder.Sql("DROP TABLE IF EXISTS `EmploymentDateOfJoiningMigrationConflicts`;");
        }
    }
}
