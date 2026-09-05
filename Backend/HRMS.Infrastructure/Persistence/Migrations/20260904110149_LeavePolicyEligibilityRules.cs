using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeavePolicyEligibilityRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeavePolicyEligibilityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EligibilityMode = table.Column<int>(type: "int", nullable: false),
                    MinimumServiceValue = table.Column<int>(type: "int", nullable: true),
                    MinimumServiceUnit = table.Column<int>(type: "int", nullable: true),
                    ProbationMode = table.Column<int>(type: "int", nullable: false),
                    NoticePeriodMode = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyEligibilityRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyEligibilityRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyEligibilityRules_LeavePolicyRules_TenantId_LeavePolicyRuleId",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyEligibilityRules_TenantId_LeavePolicyRuleId",
                table: "LeavePolicyEligibilityRules",
                columns: new[] { "TenantId", "LeavePolicyRuleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LeavePolicyEligibilityRules");
        }
    }
}
