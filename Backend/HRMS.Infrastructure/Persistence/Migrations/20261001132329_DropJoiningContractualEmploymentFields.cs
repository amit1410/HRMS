using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropJoiningContractualEmploymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[EmployeeEmploymentLegacyFields]', N'U') IS NULL
CREATE TABLE [dbo].[EmployeeEmploymentLegacyFields](
    [TenantId] uniqueidentifier NOT NULL,
    [EmployeeId] uniqueidentifier NOT NULL,
    [FirstHiredDate] date NOT NULL,
    [GroupDateOfJoining] date NULL,
    [ProbationPeriod] int NULL,
    [ProbationPeriodUnit] nvarchar(20) NULL,
    [ArchivedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_EmployeeEmploymentLegacyFields] PRIMARY KEY ([TenantId], [EmployeeId])
);

INSERT INTO [dbo].[EmployeeEmploymentLegacyFields]
    ([TenantId], [EmployeeId], [FirstHiredDate], [GroupDateOfJoining], [ProbationPeriod], [ProbationPeriodUnit], [ArchivedAtUtc])
SELECT ee.[TenantId], ee.[EmployeeId], ee.[FirstHiredDate], ee.[GroupDateOfJoining], ee.[ProbationPeriod], ee.[ProbationPeriodUnit], SYSUTCDATETIME()
FROM [EmployeeEmployments] ee
WHERE NOT EXISTS (
    SELECT 1 FROM [dbo].[EmployeeEmploymentLegacyFields] archived
    WHERE archived.[TenantId] = ee.[TenantId] AND archived.[EmployeeId] = ee.[EmployeeId]);");

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
            migrationBuilder.AddColumn<DateOnly>(
                name: "FirstHiredDate",
                table: "EmployeeEmployments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
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
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE ee
SET [FirstHiredDate] = archived.[FirstHiredDate],
    [GroupDateOfJoining] = archived.[GroupDateOfJoining],
    [ProbationPeriod] = archived.[ProbationPeriod],
    [ProbationPeriodUnit] = archived.[ProbationPeriodUnit]
FROM [EmployeeEmployments] ee
INNER JOIN [dbo].[EmployeeEmploymentLegacyFields] archived
    ON archived.[TenantId] = ee.[TenantId] AND archived.[EmployeeId] = ee.[EmployeeId];");
        }
    }
}
