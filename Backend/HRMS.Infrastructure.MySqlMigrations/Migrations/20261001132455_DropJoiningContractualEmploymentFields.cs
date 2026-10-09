using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class DropJoiningContractualEmploymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `EmployeeEmploymentLegacyFields` (
    `TenantId` char(36) NOT NULL,
    `EmployeeId` char(36) NOT NULL,
    `FirstHiredDate` date NOT NULL,
    `GroupDateOfJoining` date NULL,
    `ProbationPeriod` int NULL,
    `ProbationPeriodUnit` varchar(20) NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`TenantId`, `EmployeeId`)
);");

            migrationBuilder.Sql(@"
INSERT IGNORE INTO `EmployeeEmploymentLegacyFields`
    (`TenantId`, `EmployeeId`, `FirstHiredDate`, `GroupDateOfJoining`, `ProbationPeriod`, `ProbationPeriodUnit`, `ArchivedAtUtc`)
SELECT ee.`TenantId`, ee.`EmployeeId`, ee.`FirstHiredDate`, ee.`GroupDateOfJoining`, ee.`ProbationPeriod`, ee.`ProbationPeriodUnit`, UTC_TIMESTAMP(6)
FROM `EmployeeEmployments` ee;");

            migrationBuilder.DropColumn(
                name: "FirstHiredDate",
                table: "EmployeeEmployments");

            migrationBuilder.DropColumn(
                name: "GroupDateOfJoining",
                table: "EmployeeEmployments");

            migrationBuilder.DropColumn(
                name: "ProbationPeriod",
                table: "EmployeeEmployments");

            migrationBuilder.DropColumn(
                name: "ProbationPeriodUnit",
                table: "EmployeeEmployments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstHiredDate",
                table: "EmployeeEmployments",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "GroupDateOfJoining",
                table: "EmployeeEmployments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProbationPeriod",
                table: "EmployeeEmployments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProbationPeriodUnit",
                table: "EmployeeEmployments",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE `EmployeeEmployments` ee
INNER JOIN `EmployeeEmploymentLegacyFields` archived
    ON archived.`TenantId` = ee.`TenantId` AND archived.`EmployeeId` = ee.`EmployeeId`
SET ee.`FirstHiredDate` = archived.`FirstHiredDate`,
    ee.`GroupDateOfJoining` = archived.`GroupDateOfJoining`,
    ee.`ProbationPeriod` = archived.`ProbationPeriod`,
    ee.`ProbationPeriodUnit` = archived.`ProbationPeriodUnit`;");
        }
    }
}
