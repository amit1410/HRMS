using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

public partial class LeavePolicyFoundationHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LowerLeavePolicyRuleId",
            table: "LeavePolicyClubbingRules");
        migrationBuilder.DropForeignKey(
            name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_HigherLeavePolicyRuleId",
            table: "LeavePolicyClubbingRules");
        migrationBuilder.DropIndex(
            name: "IX_LeavePolicyClubbingRules_TenantId_Version_Lower_Higher",
            table: "LeavePolicyClubbingRules");

        migrationBuilder.AddUniqueConstraint(
            name: "AK_LeavePolicyRules_TenantId_LeavePolicyVersionId_Id",
            table: "LeavePolicyRules",
            columns: new[] { "TenantId", "LeavePolicyVersionId", "Id" });

        migrationBuilder.AddColumn<string>(
            name: "NormalizedPairKey",
            table: "LeavePolicyClubbingRules",
            type: "varchar(73)",
            maxLength: 73,
            nullable: false,
            computedColumnSql: "CASE WHEN CONVERT(varchar(36), [LowerLeavePolicyRuleId]) < CONVERT(varchar(36), [HigherLeavePolicyRuleId]) THEN CONVERT(varchar(36), [LowerLeavePolicyRuleId]) + ':' + CONVERT(varchar(36), [HigherLeavePolicyRuleId]) ELSE CONVERT(varchar(36), [HigherLeavePolicyRuleId]) + ':' + CONVERT(varchar(36), [LowerLeavePolicyRuleId]) END",
            stored: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_LeavePolicyClubbingRules_DifferentParticipants",
            table: "LeavePolicyClubbingRules",
            sql: "[LowerLeavePolicyRuleId] <> [HigherLeavePolicyRuleId]");

        migrationBuilder.CreateIndex(
            name: "IX_LeavePolicyClubbingRules_TenantId_LeavePolicyVersionId_NormalizedPairKey",
            table: "LeavePolicyClubbingRules",
            columns: new[] { "TenantId", "LeavePolicyVersionId", "NormalizedPairKey" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LeavePolicyVersionId_LowerLeavePolicyRuleId",
            table: "LeavePolicyClubbingRules",
            columns: new[] { "TenantId", "LeavePolicyVersionId", "LowerLeavePolicyRuleId" },
            principalTable: "LeavePolicyRules",
            principalColumns: new[] { "TenantId", "LeavePolicyVersionId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LeavePolicyVersionId_HigherLeavePolicyRuleId",
            table: "LeavePolicyClubbingRules",
            columns: new[] { "TenantId", "LeavePolicyVersionId", "HigherLeavePolicyRuleId" },
            principalTable: "LeavePolicyRules",
            principalColumns: new[] { "TenantId", "LeavePolicyVersionId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LeavePolicyVersionId_LowerLeavePolicyRuleId", "LeavePolicyClubbingRules");
        migrationBuilder.DropForeignKey("FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LeavePolicyVersionId_HigherLeavePolicyRuleId", "LeavePolicyClubbingRules");
        migrationBuilder.DropIndex("IX_LeavePolicyClubbingRules_TenantId_LeavePolicyVersionId_NormalizedPairKey", "LeavePolicyClubbingRules");
        migrationBuilder.DropCheckConstraint("CK_LeavePolicyClubbingRules_DifferentParticipants", "LeavePolicyClubbingRules");
        migrationBuilder.DropColumn("NormalizedPairKey", "LeavePolicyClubbingRules");
        migrationBuilder.DropUniqueConstraint("AK_LeavePolicyRules_TenantId_LeavePolicyVersionId_Id", "LeavePolicyRules");
        migrationBuilder.CreateIndex("IX_LeavePolicyClubbingRules_TenantId_Version_Lower_Higher", "LeavePolicyClubbingRules", new[] { "TenantId", "LeavePolicyVersionId", "LowerLeavePolicyRuleId", "HigherLeavePolicyRuleId" }, unique: true);
        migrationBuilder.AddForeignKey(
            name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LowerLeavePolicyRuleId",
            table: "LeavePolicyClubbingRules",
            columns: new[] { "TenantId", "LowerLeavePolicyRuleId" },
            principalTable: "LeavePolicyRules",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_HigherLeavePolicyRuleId",
            table: "LeavePolicyClubbingRules",
            columns: new[] { "TenantId", "HigherLeavePolicyRuleId" },
            principalTable: "LeavePolicyRules",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }
}
