using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
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
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecisionMakerUserId",
                table: "ConfirmationReviewCases",
                type: "char(36)",
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
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotConfirmedJobStatus",
                table: "ConfirmationPolicyRules",
                type: "varchar(100)",
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
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConfirmationDecisionCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    AppliedRuleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CalculatedOutcome = table.Column<int>(type: "int", nullable: false),
                    Explanation = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationDecisionCalculations", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationDecisionCalculations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationDecisionCalculations_ConfirmationReviewCases_Ten~",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseId },
                        principalTable: "ConfirmationReviewCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationDecisionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    MinOverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    MaxOverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationDecisionRules", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationDecisionRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationDecisionRules_ConfirmationPolicyVersions_TenantI~",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyVersionId },
                        principalTable: "ConfirmationPolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProbationCycleKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    DecisionReason = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    DecisionMakerUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DecisionMakerEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CalculatedOverallRating = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    AppliedRuleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PreviousCaseStatus = table.Column<int>(type: "int", nullable: false),
                    NewCaseStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationDecisions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationDecisions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationDecisions_ConfirmationReviewCases_TenantId_Confi~",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseId },
                        principalTable: "ConfirmationReviewCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationProbationCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationPolicyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    BaselineEndDate = table.Column<DateTime>(type: "date", nullable: false),
                    CurrentEndDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExtensionCount = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationProbationExtensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationProbationCycleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PreviousEndDate = table.Column<DateTime>(type: "date", nullable: false),
                    ExtensionDuration = table.Column<int>(type: "int", nullable: false),
                    ExtensionDurationUnit = table.Column<int>(type: "int", nullable: false),
                    NewEndDate = table.Column<DateTime>(type: "date", nullable: false),
                    DecisionMakerUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Reason = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    ExtendedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationProbationExtensions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationProbationExtensions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationProbationExtensions_ConfirmationProbationCycles_~",
                        columns: x => new { x.TenantId, x.ConfirmationProbationCycleId },
                        principalTable: "ConfirmationProbationCycles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationDecisionCalculations_TenantId_ConfirmationReview~",
                table: "ConfirmationDecisionCalculations",
                columns: new[] { "TenantId", "ConfirmationReviewCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationDecisionRules_TenantId_ConfirmationPolicyVersion~",
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
                name: "IX_ConfirmationProbationExtensions_TenantId_ConfirmationProbati~",
                table: "ConfirmationProbationExtensions",
                columns: new[] { "TenantId", "ConfirmationProbationCycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationProbationExtensions_TenantId_ConfirmationReviewC~",
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
