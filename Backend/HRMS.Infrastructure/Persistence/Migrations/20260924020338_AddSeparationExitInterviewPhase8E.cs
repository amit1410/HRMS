using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationExitInterviewPhase8E : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeparationExitInterviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeSeparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmployeeSubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HrStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HrCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedHrUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrimaryReasonCategory = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    SecondaryReasonCategory = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ReasonComment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RehireRecommendation = table.Column<int>(type: "int", nullable: false),
                    RehireRecommendationReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CompletedWithoutEmployeeResponse = table.Column<bool>(type: "bit", nullable: false),
                    CompletionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviews", x => x.Id);
                    table.UniqueConstraint("AK_SeparationExitInterviews_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviews_EmployeeSeparations_TenantId_EmployeeSeparationId",
                        columns: x => new { x.TenantId, x.EmployeeSeparationId },
                        principalTable: "EmployeeSeparations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviews_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    AppliesToSeparationType = table.Column<int>(type: "int", nullable: true),
                    AppliesToReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsEmployeeSurveyEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsHrInterviewEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewTemplates", x => x.Id);
                    table.UniqueConstraint("AK_SeparationExitInterviewTemplates_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExitInterviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewEvents_SeparationExitInterviews_TenantId_ExitInterviewId",
                        columns: x => new { x.TenantId, x.ExitInterviewId },
                        principalTable: "SeparationExitInterviews",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewHrNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExitInterviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    IsConfidential = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewHrNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewHrNotes_SeparationExitInterviews_TenantId_ExitInterviewId",
                        columns: x => new { x.TenantId, x.ExitInterviewId },
                        principalTable: "SeparationExitInterviews",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewHrNotes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewResponseRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExitInterviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    NumericValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    SelectedOptionCodes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewResponseRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewResponseRevisions_SeparationExitInterviews_TenantId_ExitInterviewId",
                        columns: x => new { x.TenantId, x.ExitInterviewId },
                        principalTable: "SeparationExitInterviews",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewResponseRevisions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExitInterviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    NumericValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    SelectedOptionCodes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SubmittedByEmployee = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewResponses_SeparationExitInterviews_TenantId_ExitInterviewId",
                        columns: x => new { x.TenantId, x.ExitInterviewId },
                        principalTable: "SeparationExitInterviews",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewResponses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewTemplateVersions", x => x.Id);
                    table.UniqueConstraint("AK_SeparationExitInterviewTemplateVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewTemplateVersions_SeparationExitInterviewTemplates_TenantId_TemplateId",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "SeparationExitInterviewTemplates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewTemplateVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsEmployeeVisible = table.Column<bool>(type: "bit", nullable: false),
                    IsHrOnly = table.Column<bool>(type: "bit", nullable: false),
                    AllowsComment = table.Column<bool>(type: "bit", nullable: false),
                    MinValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewQuestions", x => x.Id);
                    table.UniqueConstraint("AK_SeparationExitInterviewQuestions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewQuestions_SeparationExitInterviewTemplateVersions_TenantId_TemplateVersionId",
                        columns: x => new { x.TenantId, x.TemplateVersionId },
                        principalTable: "SeparationExitInterviewTemplateVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewQuestions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitInterviewQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitInterviewQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewQuestionOptions_SeparationExitInterviewQuestions_TenantId_QuestionId",
                        columns: x => new { x.TenantId, x.QuestionId },
                        principalTable: "SeparationExitInterviewQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitInterviewQuestionOptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewEvents_TenantId_ExitInterviewId_OccurredAtUtc",
                table: "SeparationExitInterviewEvents",
                columns: new[] { "TenantId", "ExitInterviewId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewHrNotes_TenantId_ExitInterviewId_CreatedDate",
                table: "SeparationExitInterviewHrNotes",
                columns: new[] { "TenantId", "ExitInterviewId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewQuestionOptions_TenantId_QuestionId_Code",
                table: "SeparationExitInterviewQuestionOptions",
                columns: new[] { "TenantId", "QuestionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewQuestions_TenantId_TemplateVersionId_Code",
                table: "SeparationExitInterviewQuestions",
                columns: new[] { "TenantId", "TemplateVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewQuestions_TenantId_TemplateVersionId_Sequence",
                table: "SeparationExitInterviewQuestions",
                columns: new[] { "TenantId", "TemplateVersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewResponseRevisions_TenantId_ExitInterviewId_QuestionId_RecordedAtUtc",
                table: "SeparationExitInterviewResponseRevisions",
                columns: new[] { "TenantId", "ExitInterviewId", "QuestionId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewResponses_TenantId_ExitInterviewId_QuestionId",
                table: "SeparationExitInterviewResponses",
                columns: new[] { "TenantId", "ExitInterviewId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviews_TenantId_AssignedHrUserId_Status",
                table: "SeparationExitInterviews",
                columns: new[] { "TenantId", "AssignedHrUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviews_TenantId_EmployeeSeparationId",
                table: "SeparationExitInterviews",
                columns: new[] { "TenantId", "EmployeeSeparationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviews_TenantId_Status",
                table: "SeparationExitInterviews",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewTemplates_TenantId_Code",
                table: "SeparationExitInterviewTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewTemplates_TenantId_EffectiveFrom_EffectiveTo",
                table: "SeparationExitInterviewTemplates",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewTemplateVersions_TenantId_Status_EffectiveFrom",
                table: "SeparationExitInterviewTemplateVersions",
                columns: new[] { "TenantId", "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitInterviewTemplateVersions_TenantId_TemplateId_VersionNumber",
                table: "SeparationExitInterviewTemplateVersions",
                columns: new[] { "TenantId", "TemplateId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeparationExitInterviewEvents");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviewHrNotes");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviewQuestionOptions");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviewResponseRevisions");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviewResponses");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviewQuestions");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviews");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviewTemplateVersions");

            migrationBuilder.DropTable(
                name: "SeparationExitInterviewTemplates");
        }
    }
}
