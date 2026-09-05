using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace HRMS.Infrastructure.Persistence.Migrations;
public partial class LeavePolicyCalendarRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("LeavePolicyCalendarRules", table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            HolidayTreatment = table.Column<int>(type: "int", nullable: false), WeekOffTreatment = table.Column<int>(type: "int", nullable: false), SandwichMode = table.Column<int>(type: "int", nullable: false),
            ApplyToPrefix = table.Column<bool>(type: "bit", nullable: false), ApplyToSuffix = table.Column<bool>(type: "bit", nullable: false), ApplyToBetween = table.Column<bool>(type: "bit", nullable: false),
            CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false), ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_LeavePolicyCalendarRules", x => x.Id); table.UniqueConstraint("AK_LeavePolicyCalendarRules_TenantId_Id", x => new { x.TenantId, x.Id });
            table.ForeignKey("FK_LeavePolicyCalendarRules_LeavePolicyRules_TenantId_LeavePolicyRuleId", x => new { x.TenantId, x.LeavePolicyRuleId }, "LeavePolicyRules", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_LeavePolicyCalendarRules_TenantId_LeavePolicyRuleId", "LeavePolicyCalendarRules", new[] { "TenantId", "LeavePolicyRuleId" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("LeavePolicyCalendarRules");
}
