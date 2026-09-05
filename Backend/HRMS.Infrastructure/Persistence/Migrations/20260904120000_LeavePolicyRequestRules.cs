using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

public partial class LeavePolicyRequestRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LeavePolicyRequestRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MinimumRequestQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                MaximumRequestQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                MaximumConsecutiveQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                MinimumAdvanceNoticeDays = table.Column<int>(type: "int", nullable: false),
                BackdatedRequestMode = table.Column<int>(type: "int", nullable: false),
                MaximumBackdatedDays = table.Column<int>(type: "int", nullable: true),
                MaximumRequestsPerPeriod = table.Column<int>(type: "int", nullable: true),
                MaximumQuantityPerPeriod = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                RequestLimitPeriod = table.Column<int>(type: "int", nullable: true),
                PartialDayMode = table.Column<int>(type: "int", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_LeavePolicyRequestRules", x => x.Id);
                table.UniqueConstraint("AK_LeavePolicyRequestRules_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_LeavePolicyRequestRules_LeavePolicyRules_TenantId_LeavePolicyRuleId", x => new { x.TenantId, x.LeavePolicyRuleId }, "LeavePolicyRules", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_LeavePolicyRequestRules_TenantId_LeavePolicyRuleId", "LeavePolicyRequestRules", new[] { "TenantId", "LeavePolicyRuleId" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("LeavePolicyRequestRules");
}
