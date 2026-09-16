using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRMS.Infrastructure.Persistence;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20260914152734_AddEmploymentHistoryRevisions")]
public partial class AddEmploymentHistoryRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "RevisionNumber", table: "EmployeeEmploymentHistory", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<bool>(name: "IsSuperseded", table: "EmployeeEmploymentHistory", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>(name: "SupersededAtUtc", table: "EmployeeEmploymentHistory", type: "datetime(6)", nullable: true);
        migrationBuilder.Sql(@"
UPDATE EmployeeEmploymentHistory h
JOIN (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY TenantId, EmployeeId, EffectiveFrom ORDER BY CreatedDate, Id) AS RevisionNumber
    FROM EmployeeEmploymentHistory
) ranked ON ranked.Id = h.Id
SET h.RevisionNumber = ranked.RevisionNumber;");
        migrationBuilder.Sql(@"
UPDATE EmployeeEmploymentHistory h
JOIN (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY TenantId, EmployeeId, EffectiveFrom ORDER BY RevisionNumber DESC) AS ReverseRank
    FROM EmployeeEmploymentHistory
) ranked ON ranked.Id = h.Id
SET h.IsSuperseded = 1, h.SupersededAtUtc = UTC_TIMESTAMP(6)
WHERE ranked.ReverseRank > 1;");
        migrationBuilder.CreateIndex(name: "IX_EmpHist_EffectiveRevision", table: "EmployeeEmploymentHistory", columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom", "RevisionNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_EmpHist_EffectiveRevision", table: "EmployeeEmploymentHistory");
        migrationBuilder.DropColumn(name: "RevisionNumber", table: "EmployeeEmploymentHistory");
        migrationBuilder.DropColumn(name: "IsSuperseded", table: "EmployeeEmploymentHistory");
        migrationBuilder.DropColumn(name: "SupersededAtUtc", table: "EmployeeEmploymentHistory");
    }
}
