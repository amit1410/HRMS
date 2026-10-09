using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropEmploymentDateOfJoining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Date of joining becomes single-sourced from Employee (Personal Details). Before dropping
            // the duplicate EmployeeEmployments.DateOfJoining column:
            //   1) Preserve: backfill Employee.DateOfJoining from the employment record only where the
            //      employee's own value was never set (the DateOnly default 0001-01-01). This never
            //      overwrites a real Personal-Details value.
            //   2) Report: capture any remaining divergence (employment value differs from the
            //      authoritative employee value) into a standalone audit table so it can be reviewed
            //      after the column is gone, instead of being discarded silently.
            migrationBuilder.Sql(@"
UPDATE e
SET e.[DateOfJoining] = ee.[DateOfJoining]
FROM [Employees] e
INNER JOIN [EmployeeEmployments] ee ON ee.[EmployeeId] = e.[Id] AND ee.[TenantId] = e.[TenantId]
WHERE e.[DateOfJoining] = '0001-01-01' AND ee.[DateOfJoining] <> '0001-01-01';");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[EmploymentDateOfJoiningMigrationConflicts]', N'U') IS NULL
CREATE TABLE [dbo].[EmploymentDateOfJoiningMigrationConflicts](
    [TenantId] uniqueidentifier NOT NULL,
    [EmployeeId] uniqueidentifier NOT NULL,
    [EmployeeDateOfJoining] date NOT NULL,
    [EmploymentDateOfJoining] date NOT NULL,
    [CapturedAtUtc] datetime2 NOT NULL
);");

            migrationBuilder.Sql(@"
INSERT INTO [dbo].[EmploymentDateOfJoiningMigrationConflicts]
    ([TenantId], [EmployeeId], [EmployeeDateOfJoining], [EmploymentDateOfJoining], [CapturedAtUtc])
SELECT e.[TenantId], e.[Id], e.[DateOfJoining], ee.[DateOfJoining], SYSUTCDATETIME()
FROM [Employees] e
INNER JOIN [EmployeeEmployments] ee ON ee.[EmployeeId] = e.[Id] AND ee.[TenantId] = e.[TenantId]
WHERE ee.[DateOfJoining] <> e.[DateOfJoining];");

            migrationBuilder.DropColumn(
                name: "DateOfJoining",
                table: "EmployeeEmployments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfJoining",
                table: "EmployeeEmployments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Re-hydrate the restored mirror column from the authoritative Employee value.
            migrationBuilder.Sql(@"
UPDATE ee
SET ee.[DateOfJoining] = e.[DateOfJoining]
FROM [EmployeeEmployments] ee
INNER JOIN [Employees] e ON e.[Id] = ee.[EmployeeId] AND e.[TenantId] = ee.[TenantId];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[EmploymentDateOfJoiningMigrationConflicts]', N'U') IS NOT NULL
DROP TABLE [dbo].[EmploymentDateOfJoiningMigrationConflicts];");
        }
    }
}
