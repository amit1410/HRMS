using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationReviewMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfirmationRatingScales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MinRating = table.Column<int>(type: "int", nullable: false),
                    MaxRating = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationRatingScales", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationRatingScales_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationRatingScales_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationRatingScaleValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationRatingScaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingValue = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationRatingScaleValues", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationRatingScaleValues_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationRatingScaleValues_ConfirmationRatingScales_TenantId_ConfirmationRatingScaleId",
                        columns: x => new { x.TenantId, x.ConfirmationRatingScaleId },
                        principalTable: "ConfirmationRatingScales",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HelpText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ConfirmationRatingScaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewQuestions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewQuestions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestions_ConfirmationRatingScales_TenantId_ConfirmationRatingScaleId",
                        columns: x => new { x.TenantId, x.ConfirmationRatingScaleId },
                        principalTable: "ConfirmationRatingScales",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewQuestionApplicabilitySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: true),
                    HoldingCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FunctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubFunctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DesignationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CountryLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewQuestionApplicabilitySets", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewQuestionApplicabilitySets_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_ConfirmationPolicies_TenantId_ConfirmationPolicyId",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyId },
                        principalTable: "ConfirmationPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_ConfirmationReviewQuestions_TenantId_ConfirmationReviewQuestionId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewQuestionId },
                        principalTable: "ConfirmationReviewQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_CostCenters_TenantId_CostCenterId",
                        columns: x => new { x.TenantId, x.CostCenterId },
                        principalTable: "CostCenters",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Countries_CountryLocationId",
                        column: x => x.CountryLocationId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Designations_TenantId_DesignationId",
                        columns: x => new { x.TenantId, x.DesignationId },
                        principalTable: "Designations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_EmployeeTypes_TenantId_EmployeeTypeId",
                        columns: x => new { x.TenantId, x.EmployeeTypeId },
                        principalTable: "EmployeeTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Functions_TenantId_FunctionId",
                        columns: x => new { x.TenantId, x.FunctionId },
                        principalTable: "Functions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Grades_TenantId_GradeId",
                        columns: x => new { x.TenantId, x.GradeId },
                        principalTable: "Grades",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_HoldingCompanies_TenantId_HoldingCompanyId",
                        columns: x => new { x.TenantId, x.HoldingCompanyId },
                        principalTable: "HoldingCompanies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_LinesOfBusiness_TenantId_LobId",
                        columns: x => new { x.TenantId, x.LobId },
                        principalTable: "LinesOfBusiness",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Organisations_TenantId_OrganisationId",
                        columns: x => new { x.TenantId, x.OrganisationId },
                        principalTable: "Organisations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Sections_TenantId_SectionId",
                        columns: x => new { x.TenantId, x.SectionId },
                        principalTable: "Sections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_SubDepartments_TenantId_SubDepartmentId",
                        columns: x => new { x.TenantId, x.SubDepartmentId },
                        principalTable: "SubDepartments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_SubFunctions_TenantId_SubFunctionId",
                        columns: x => new { x.TenantId, x.SubFunctionId },
                        principalTable: "SubFunctions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_SubSections_TenantId_SubSectionId",
                        columns: x => new { x.TenantId, x.SubSectionId },
                        principalTable: "SubSections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_WorkLocations_TenantId_WorkLocationId",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationReviewQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewQuestionOptions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewQuestionOptions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionOptions_ConfirmationReviewQuestions_TenantId_ConfirmationReviewQuestionId",
                        columns: x => new { x.TenantId, x.ConfirmationReviewQuestionId },
                        principalTable: "ConfirmationReviewQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationRatingScales_TenantId_Code",
                table: "ConfirmationRatingScales",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationRatingScaleValues_TenantId_ConfirmationRatingScaleId",
                table: "ConfirmationRatingScaleValues",
                columns: new[] { "TenantId", "ConfirmationRatingScaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationRatingScaleValues_TenantId_ConfirmationRatingScaleId_RatingValue",
                table: "ConfirmationRatingScaleValues",
                columns: new[] { "TenantId", "ConfirmationRatingScaleId", "RatingValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_CountryLocationId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                column: "CountryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_ConfirmationPolicyId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "ConfirmationPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_ConfirmationReviewQuestionId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "ConfirmationReviewQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_CostCenterId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_DepartmentId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_DesignationId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "DesignationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_EmployeeTypeId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "EmployeeTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_FunctionId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "FunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_GradeId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "GradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_HoldingCompanyId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "HoldingCompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_LobId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "LobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_OrganisationId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "OrganisationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_SectionId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_SubDepartmentId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SubDepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_SubFunctionId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SubFunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_SubSectionId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SubSectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_WorkLocationId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "WorkLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionOptions_TenantId_ConfirmationReviewQuestionId",
                table: "ConfirmationReviewQuestionOptions",
                columns: new[] { "TenantId", "ConfirmationReviewQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionOptions_TenantId_ConfirmationReviewQuestionId_Code",
                table: "ConfirmationReviewQuestionOptions",
                columns: new[] { "TenantId", "ConfirmationReviewQuestionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestions_TenantId_Code",
                table: "ConfirmationReviewQuestions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestions_TenantId_ConfirmationRatingScaleId",
                table: "ConfirmationReviewQuestions",
                columns: new[] { "TenantId", "ConfirmationRatingScaleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfirmationRatingScaleValues");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewQuestionApplicabilitySets");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewQuestionOptions");

            migrationBuilder.DropTable(
                name: "ConfirmationReviewQuestions");

            migrationBuilder.DropTable(
                name: "ConfirmationRatingScales");
        }
    }
}
