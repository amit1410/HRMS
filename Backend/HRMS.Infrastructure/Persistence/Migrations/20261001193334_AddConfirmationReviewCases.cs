using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationReviewCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfirmationReviewCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProbationCycleKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProbationStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProbationEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ManagerReviewerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HrbpReviewerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewCases", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewCases_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCases_ConfirmationPolicies_TenantId_ConfirmationPolicyId",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyId },
                        principalTable: "ConfirmationPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCases_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCases_Employees_TenantId_HrbpReviewerEmployeeId",
                        columns: x => new { x.TenantId, x.HrbpReviewerEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCases_Employees_TenantId_ManagerReviewerEmployeeId",
                        columns: x => new { x.TenantId, x.ManagerReviewerEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCases_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewCaseEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewerReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewCaseEvents", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewCaseEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCaseEvents_ConfirmationReviewCases_TenantId_ConfirmationReviewCaseId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseId },
                        principalTable: "ConfirmationReviewCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewCaseQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HelpText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    SourceRatingScaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RatingScaleCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RatingScaleName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RatingMin = table.Column<int>(type: "int", nullable: true),
                    RatingMax = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewCaseQuestions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewCaseQuestions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCaseQuestions_ConfirmationReviewCases_TenantId_ConfirmationReviewCaseId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseId },
                        principalTable: "ConfirmationReviewCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewerReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerType = table.Column<int>(type: "int", nullable: false),
                    ReviewerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OverallComments = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewerReviews", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewerReviews_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewerReviews_ConfirmationReviewCases_TenantId_ConfirmationReviewCaseId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseId },
                        principalTable: "ConfirmationReviewCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewCaseQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewCaseQuestionOptions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewCaseQuestionOptions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCaseQuestionOptions_ConfirmationReviewCaseQuestions_TenantId_ConfirmationReviewCaseQuestionId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId },
                        principalTable: "ConfirmationReviewCaseQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewCaseRatingValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingValue = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewCaseRatingValues", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewCaseRatingValues_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewCaseRatingValues_ConfirmationReviewCaseQuestions_TenantId_ConfirmationReviewCaseQuestionId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId },
                        principalTable: "ConfirmationReviewCaseQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewerReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewCaseQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingValue = table.Column<int>(type: "int", nullable: true),
                    BoolValue = table.Column<bool>(type: "bit", nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewResponses", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewResponses_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewResponses_ConfirmationReviewCaseQuestions_TenantId_ConfirmationReviewCaseQuestionId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId },
                        principalTable: "ConfirmationReviewCaseQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewResponses_ConfirmationReviewerReviews_TenantId_ConfirmationReviewerReviewId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewerReviewId },
                        principalTable: "ConfirmationReviewerReviews",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewResponseOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewResponseOptions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewResponseOptions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewResponseOptions_ConfirmationReviewResponses_TenantId_ConfirmationReviewResponseId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewResponseId },
                        principalTable: "ConfirmationReviewResponses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCaseEvents_TenantId_ConfirmationReviewCaseId",
                table: "ConfirmationReviewCaseEvents",
                columns: new[] { "TenantId", "ConfirmationReviewCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCaseQuestionOptions_TenantId_ConfirmationReviewCaseQuestionId",
                table: "ConfirmationReviewCaseQuestionOptions",
                columns: new[] { "TenantId", "ConfirmationReviewCaseQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCaseQuestions_TenantId_ConfirmationReviewCaseId",
                table: "ConfirmationReviewCaseQuestions",
                columns: new[] { "TenantId", "ConfirmationReviewCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCaseRatingValues_TenantId_ConfirmationReviewCaseQuestionId",
                table: "ConfirmationReviewCaseRatingValues",
                columns: new[] { "TenantId", "ConfirmationReviewCaseQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCases_TenantId_ConfirmationPolicyId",
                table: "ConfirmationReviewCases",
                columns: new[] { "TenantId", "ConfirmationPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCases_TenantId_EmployeeId_ProbationCycleKey",
                table: "ConfirmationReviewCases",
                columns: new[] { "TenantId", "EmployeeId", "ProbationCycleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCases_TenantId_HrbpReviewerEmployeeId",
                table: "ConfirmationReviewCases",
                columns: new[] { "TenantId", "HrbpReviewerEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCases_TenantId_ManagerReviewerEmployeeId",
                table: "ConfirmationReviewCases",
                columns: new[] { "TenantId", "ManagerReviewerEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewCases_TenantId_Status",
                table: "ConfirmationReviewCases",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewerType_VersionNumber",
                table: "ConfirmationReviewerReviews",
                columns: new[] { "TenantId", "ConfirmationReviewCaseId", "ReviewerType", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewResponseOptions_TenantId_ConfirmationReviewResponseId",
                table: "ConfirmationReviewResponseOptions",
                columns: new[] { "TenantId", "ConfirmationReviewResponseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewResponses_TenantId_ConfirmationReviewCaseQuestionId",
                table: "ConfirmationReviewResponses",
                columns: new[] { "TenantId", "ConfirmationReviewCaseQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewResponses_TenantId_ConfirmationReviewerReviewId",
                table: "ConfirmationReviewResponses",
                columns: new[] { "TenantId", "ConfirmationReviewerReviewId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfirmationReviewCaseEvents");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewCaseQuestionOptions");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewCaseRatingValues");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewResponseOptions");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewResponses");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewCaseQuestions");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewerReviews");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewCases");
        }
    }
}
