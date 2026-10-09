using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20261004064500_AddReviewRoundToEntities")]
public partial class AddReviewRoundToEntities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "ReviewRound", table: "ConfirmationReviewCases", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "ReviewRound", table: "ConfirmationReviewerReviews", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "ReviewRound", table: "ConfirmationDecisions", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "ReviewRound", table: "ConfirmationDecisionCalculations", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.DropIndex(name: "IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewerType_VersionNumber", table: "ConfirmationReviewerReviews");
        migrationBuilder.CreateIndex(name: "IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewRound_ReviewerType_VersionNumber", table: "ConfirmationReviewerReviews", columns: new[] { "TenantId", "ConfirmationReviewCaseId", "ReviewRound", "ReviewerType", "VersionNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewRound_ReviewerType_VersionNumber", table: "ConfirmationReviewerReviews");
        migrationBuilder.CreateIndex(name: "IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewerType_VersionNumber", table: "ConfirmationReviewerReviews", columns: new[] { "TenantId", "ConfirmationReviewCaseId", "ReviewerType", "VersionNumber" }, unique: true);
        migrationBuilder.DropColumn(name: "ReviewRound", table: "ConfirmationReviewCases");
        migrationBuilder.DropColumn(name: "ReviewRound", table: "ConfirmationReviewerReviews");
        migrationBuilder.DropColumn(name: "ReviewRound", table: "ConfirmationDecisions");
        migrationBuilder.DropColumn(name: "ReviewRound", table: "ConfirmationDecisionCalculations");
    }
}
