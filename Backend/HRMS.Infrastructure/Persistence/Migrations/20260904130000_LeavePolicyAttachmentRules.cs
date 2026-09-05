using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace HRMS.Infrastructure.Persistence.Migrations;
public partial class LeavePolicyAttachmentRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("LeavePolicyAttachmentRules", table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            AttachmentRequirement = table.Column<int>(type: "int", nullable: false), ThresholdQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true), DocumentLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true), CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false), ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_LeavePolicyAttachmentRules", x => x.Id); table.UniqueConstraint("AK_LeavePolicyAttachmentRules_TenantId_Id", x => new { x.TenantId, x.Id }); table.ForeignKey("FK_LeavePolicyAttachmentRules_LeavePolicyRules_TenantId_LeavePolicyRuleId", x => new { x.TenantId, x.LeavePolicyRuleId }, "LeavePolicyRules", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("IX_LeavePolicyAttachmentRules_TenantId_LeavePolicyRuleId", "LeavePolicyAttachmentRules", new[] { "TenantId", "LeavePolicyRuleId" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("LeavePolicyAttachmentRules");
}
