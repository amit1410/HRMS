using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

public partial class LeavePolicyEntitlementRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LeavePolicyEntitlementRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntitlementMode = table.Column<int>(type: "int", nullable: false),
                EntitlementSource = table.Column<int>(type: "int", nullable: false),
                EntitlementQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                AccrualFrequency = table.Column<int>(type: "int", nullable: false),
                AccrualTiming = table.Column<int>(type: "int", nullable: true),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LeavePolicyEntitlementRules", x => x.Id);
                table.UniqueConstraint("AK_LeavePolicyEntitlementRules_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey(
                    name: "FK_LeavePolicyEntitlementRules_LeavePolicyRules_TenantId_LeavePolicyRuleId",
                    columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                    principalTable: "LeavePolicyRules",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_LeavePolicyEntitlementRules_TenantId_LeavePolicyRuleId", "LeavePolicyEntitlementRules", new[] { "TenantId", "LeavePolicyRuleId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("LeavePolicyEntitlementRules");
}
