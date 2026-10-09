using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationDecisionEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CycleNumber",
                table: "ConfirmationReviewCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAtUtc",
                table: "ConfirmationReviewCases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecisionMakerUserId",
                table: "ConfirmationReviewCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Outcome",
                table: "ConfirmationReviewCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalSequence",
                table: "ConfirmationPolicyRules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedJobStatus",
                table: "ConfirmationPolicyRules",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotConfirmedJobStatus",
                table: "ConfirmationPolicyRules",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverallRatingMethod",
                table: "ConfirmationPolicyRules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequireReviewerComments",
                table: "ConfirmationPolicyRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConfirmationDecisionCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    AppliedRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CalculatedOutcome = table.Column<int>(type: "int", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationDecisionCalculations", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationDecisionCalculations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationDecisionCalculations_ConfirmationReviewCases_TenantId_ConfirmationReviewCaseId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseId },
                        principalTable: "ConfirmationReviewCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationDecisionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MinOverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    MaxOverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationDecisionRules", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationDecisionRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationDecisionRules_ConfirmationPolicyVersions_TenantId_ConfirmationPolicyVersionId",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyVersionId },
                        principalTable: "ConfirmationPolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProbationCycleKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecisionMakerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionMakerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalculatedOverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    AppliedRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousCaseStatus = table.Column<int>(type: "int", nullable: false),
                    NewCaseStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationDecisions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationDecisions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationDecisions_ConfirmationReviewCases_TenantId_ConfirmationReviewCaseId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseId },
                        principalTable: "ConfirmationReviewCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationProbationCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BaselineEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExtensionCount = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationProbationCycles", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationProbationCycles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationProbationCycles_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationProbationCycles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationProbationExtensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationProbationCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExtensionDuration = table.Column<int>(type: "int", nullable: false),
                    ExtensionDurationUnit = table.Column<int>(type: "int", nullable: false),
                    NewEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DecisionMakerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ExtendedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationProbationExtensions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationProbationExtensions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationProbationExtensions_ConfirmationProbationCycles_TenantId_ConfirmationProbationCycleId",
                        columns: x => new { x.TenantId, x.ConfirmationProbationCycleId },
                        principalTable: "ConfirmationProbationCycles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationDecisionCalculations_TenantId_ConfirmationReviewCaseId",
                table: "ConfirmationDecisionCalculations",
                columns: new[] { "TenantId", "ConfirmationReviewCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationDecisionRules_TenantId_ConfirmationPolicyVersionId_Priority",
                table: "ConfirmationDecisionRules",
                columns: new[] { "TenantId", "ConfirmationPolicyVersionId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationDecisions_TenantId_ConfirmationReviewCaseId",
                table: "ConfirmationDecisions",
                columns: new[] { "TenantId", "ConfirmationReviewCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationProbationCycles_TenantId_EmployeeId_CycleNumber",
                table: "ConfirmationProbationCycles",
                columns: new[] { "TenantId", "EmployeeId", "CycleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationProbationCycles_TenantId_EmployeeId_Status",
                table: "ConfirmationProbationCycles",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationProbationExtensions_TenantId_ConfirmationProbationCycleId",
                table: "ConfirmationProbationExtensions",
                columns: new[] { "TenantId", "ConfirmationProbationCycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationProbationExtensions_TenantId_ConfirmationReviewCaseId",
                table: "ConfirmationProbationExtensions",
                columns: new[] { "TenantId", "ConfirmationReviewCaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfirmationDecisionCalculations");

            migrationBuilder.DropTable(
                name: "ConfirmationDecisionRules");

            migrationBuilder.DropTable(
                name: "ConfirmationDecisions");

            migrationBuilder.DropTable(
                name: "ConfirmationProbationExtensions");

            migrationBuilder.DropTable(
                name: "ConfirmationProbationCycles");

            migrationBuilder.DropColumn(
                name: "CycleNumber",
                table: "ConfirmationReviewCases");

            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                table: "ConfirmationReviewCases");

            migrationBuilder.DropColumn(
                name: "DecisionMakerUserId",
                table: "ConfirmationReviewCases");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "ConfirmationReviewCases");

            migrationBuilder.DropColumn(
                name: "ApprovalSequence",
                table: "ConfirmationPolicyRules");

            migrationBuilder.DropColumn(
                name: "ConfirmedJobStatus",
                table: "ConfirmationPolicyRules");

            migrationBuilder.DropColumn(
                name: "NotConfirmedJobStatus",
                table: "ConfirmationPolicyRules");

            migrationBuilder.DropColumn(
                name: "OverallRatingMethod",
                table: "ConfirmationPolicyRules");

            migrationBuilder.DropColumn(
                name: "RequireReviewerComments",
                table: "ConfirmationPolicyRules");
        }
    }
}
