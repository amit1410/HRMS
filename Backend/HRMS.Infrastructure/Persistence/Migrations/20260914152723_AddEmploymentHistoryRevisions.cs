using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRMS.Infrastructure.Persistence;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20260914152723_AddEmploymentHistoryRevisions")]
public partial class AddEmploymentHistoryRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "RevisionNumber", table: "EmployeeEmploymentHistory", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<bool>(name: "IsSuperseded", table: "EmployeeEmploymentHistory", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>(name: "SupersededAtUtc", table: "EmployeeEmploymentHistory", type: "datetime2", nullable: true);
        migrationBuilder.Sql(@"
WITH Ranked AS
(
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY TenantId, EmployeeId, EffectiveFrom ORDER BY CreatedDate, Id) AS RevisionNumber
    FROM EmployeeEmploymentHistory
)
UPDATE h SET RevisionNumber = r.RevisionNumber
FROM EmployeeEmploymentHistory h
INNER JOIN Ranked r ON r.Id = h.Id;");
        migrationBuilder.Sql(@"
WITH Ranked AS
(
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY TenantId, EmployeeId, EffectiveFrom ORDER BY RevisionNumber DESC) AS ReverseRank
    FROM EmployeeEmploymentHistory
)
UPDATE h SET IsSuperseded = 1, SupersededAtUtc = SYSUTCDATETIME()
FROM EmployeeEmploymentHistory h
INNER JOIN Ranked r ON r.Id = h.Id
WHERE r.ReverseRank > 1;");
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
