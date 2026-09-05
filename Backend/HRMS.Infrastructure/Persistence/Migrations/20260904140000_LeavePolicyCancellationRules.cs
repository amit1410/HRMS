using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace HRMS.Infrastructure.Persistence.Migrations;
public partial class LeavePolicyCancellationRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("LeavePolicyCancellationRules", table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), WithdrawAllowed = table.Column<bool>(type: "bit", nullable: false), CancelAllowed = table.Column<bool>(type: "bit", nullable: false), ModifyAllowed = table.Column<bool>(type: "bit", nullable: false), CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false), ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_LeavePolicyCancellationRules", x => x.Id); table.UniqueConstraint("AK_LeavePolicyCancellationRules_TenantId_Id", x => new { x.TenantId, x.Id }); table.ForeignKey("FK_LeavePolicyCancellationRules_LeavePolicyRules_TenantId_LeavePolicyRuleId", x => new { x.TenantId, x.LeavePolicyRuleId }, "LeavePolicyRules", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("IX_LeavePolicyCancellationRules_TenantId_LeavePolicyRuleId", "LeavePolicyCancellationRules", new[] { "TenantId", "LeavePolicyRuleId" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("LeavePolicyCancellationRules");
}
