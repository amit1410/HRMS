using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace HRMS.Infrastructure.Persistence.Migrations;
public partial class LeavePolicyClubbingRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("LeavePolicyClubbingRules", table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), LeavePolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), LowerLeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), HigherLeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), Relation = table.Column<int>(type: "int", nullable: false), CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false), ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_LeavePolicyClubbingRules", x => x.Id); table.UniqueConstraint("AK_LeavePolicyClubbingRules_TenantId_Id", x => new { x.TenantId, x.Id }); table.ForeignKey("FK_LeavePolicyClubbingRules_LeavePolicyVersions_TenantId_LeavePolicyVersionId", x => new { x.TenantId, x.LeavePolicyVersionId }, "LeavePolicyVersions", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LowerLeavePolicyRuleId", x => new { x.TenantId, x.LowerLeavePolicyRuleId }, "LeavePolicyRules", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_HigherLeavePolicyRuleId", x => new { x.TenantId, x.HigherLeavePolicyRuleId }, "LeavePolicyRules", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("IX_LeavePolicyClubbingRules_TenantId_Version_Lower_Higher", "LeavePolicyClubbingRules", new[] { "TenantId", "LeavePolicyVersionId", "LowerLeavePolicyRuleId", "HigherLeavePolicyRuleId" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("LeavePolicyClubbingRules");
}
