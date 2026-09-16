using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

/// <summary>Provider-equivalent repair for the employment revision schema.</summary>
[Migration("20260914210001_FixEmployeeEmploymentRevisionSchema")]
[DbContext(typeof(HrmsDbContext))]
public partial class FixEmployeeEmploymentRevisionSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RevisionNumber",
            table: "EmployeeEmploymentHistory",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<bool>(
            name: "IsSuperseded",
            table: "EmployeeEmploymentHistory",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "SupersededAtUtc",
            table: "EmployeeEmploymentHistory",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_EmpHist_EffectiveRevision",
            table: "EmployeeEmploymentHistory",
            columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom", "RevisionNumber" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_EmpHist_EffectiveRevision",
            table: "EmployeeEmploymentHistory");

        migrationBuilder.DropColumn(name: "RevisionNumber", table: "EmployeeEmploymentHistory");
        migrationBuilder.DropColumn(name: "IsSuperseded", table: "EmployeeEmploymentHistory");
        migrationBuilder.DropColumn(name: "SupersededAtUtc", table: "EmployeeEmploymentHistory");
    }
}
