using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCodeGenerationModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignmentMode",
                table: "EmployeeCodeConfigVersions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GenerationMethod",
                table: "EmployeeCodeConfigVersions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignmentMode",
                table: "EmployeeCodeConfigs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GenerationMethod",
                table: "EmployeeCodeConfigs",
                type: "int",
                nullable: true);

            // Backfill explicit modes from the legacy AutoGenerate flag. Existing active rules are
            // treated as RuleBased; otherwise the legacy prefix/sequence is preserved as Simple.
            migrationBuilder.Sql(@"
UPDATE c
SET AssignmentMode = CASE WHEN c.AutoGenerate = 1 THEN 1 ELSE 0 END,
    GenerationMethod = CASE
        WHEN c.AutoGenerate = 0 THEN NULL
        WHEN EXISTS (SELECT 1 FROM EmployeeCodeRules r WHERE r.EmployeeCodeConfigId = c.Id AND r.Status = 1) THEN 1
        ELSE 0 END
FROM EmployeeCodeConfigs c;");

            // Create one compatibility version per legacy tenant configuration, preserving all values.
            migrationBuilder.Sql(@"
INSERT INTO EmployeeCodeConfigVersions
    (Id, TenantId, EmployeeCodeConfigId, AutoGenerate, AssignmentMode, GenerationMethod,
     Prefix, Separator, NextNumber, Padding, EffectiveFrom, EffectiveTo, IsActive, CreatedDate)
SELECT NEWID(), c.TenantId, c.Id, c.AutoGenerate, c.AssignmentMode, c.GenerationMethod,
       c.Prefix, c.Separator, c.NextNumber, c.Padding, c.EffectiveFrom, c.EffectiveTo, 1, GETUTCDATE()
FROM EmployeeCodeConfigs c
WHERE NOT EXISTS (
    SELECT 1 FROM EmployeeCodeConfigVersions v
    WHERE v.TenantId = c.TenantId AND v.EmployeeCodeConfigId = c.Id
);");

            // Preserve existing rule identities and associate each unlinked rule with its tenant's
            // compatibility version. No rule, condition, segment, or sequence rows are recreated.
            migrationBuilder.Sql(@"
UPDATE r
SET EmployeeCodeConfigVersionId = v.Id
FROM EmployeeCodeRules r
JOIN EmployeeCodeConfigVersions v
  ON v.TenantId = r.TenantId AND v.EmployeeCodeConfigId = r.EmployeeCodeConfigId
WHERE r.EmployeeCodeConfigVersionId IS NULL;");

            migrationBuilder.Sql(@"
UPDATE v
SET AssignmentMode = c.AssignmentMode,
    GenerationMethod = c.GenerationMethod
FROM EmployeeCodeConfigVersions v
JOIN EmployeeCodeConfigs c ON c.TenantId = v.TenantId AND c.Id = v.EmployeeCodeConfigId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignmentMode",
                table: "EmployeeCodeConfigVersions");

            migrationBuilder.DropColumn(
                name: "GenerationMethod",
                table: "EmployeeCodeConfigVersions");

            migrationBuilder.DropColumn(
                name: "AssignmentMode",
                table: "EmployeeCodeConfigs");

            migrationBuilder.DropColumn(
                name: "GenerationMethod",
                table: "EmployeeCodeConfigs");
        }
    }
}
