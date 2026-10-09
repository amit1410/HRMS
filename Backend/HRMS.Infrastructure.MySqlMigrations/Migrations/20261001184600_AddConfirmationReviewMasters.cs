using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
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
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    MinRating = table.Column<int>(type: "int", nullable: false),
                    MaxRating = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationRatingScaleValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationRatingScaleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RatingValue = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationRatingScaleValues", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationRatingScaleValues_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationRatingScaleValues_ConfirmationRatingScales_Tenan~",
                        columns: x => new { x.TenantId, x.ConfirmationRatingScaleId },
                        principalTable: "ConfirmationRatingScales",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    QuestionText = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    HelpText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    IsMandatory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    ConfirmationRatingScaleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewQuestions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewQuestions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestions_ConfirmationRatingScales_TenantI~",
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewQuestionApplicabilitySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationReviewQuestionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationPolicyId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: true),
                    HoldingCompanyId = table.Column<Guid>(type: "char(36)", nullable: true),
                    LobId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SubDepartmentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SubSectionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    FunctionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SubFunctionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    GradeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DesignationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployeeTypeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CountryLocationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewQuestionApplicabilitySets", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewQuestionApplicabilitySets_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_ConfirmationPoli~",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyId },
                        principalTable: "ConfirmationPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_ConfirmationRevi~",
                        columns: x => new { x.TenantId, x.ConfirmationReviewQuestionId },
                        principalTable: "ConfirmationReviewQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_CostCenters_Tena~",
                        columns: x => new { x.TenantId, x.CostCenterId },
                        principalTable: "CostCenters",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Countries_Countr~",
                        column: x => x.CountryLocationId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Departments_Tena~",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Designations_Ten~",
                        columns: x => new { x.TenantId, x.DesignationId },
                        principalTable: "Designations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_EmployeeTypes_Te~",
                        columns: x => new { x.TenantId, x.EmployeeTypeId },
                        principalTable: "EmployeeTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Functions_Tenant~",
                        columns: x => new { x.TenantId, x.FunctionId },
                        principalTable: "Functions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Grades_TenantId_~",
                        columns: x => new { x.TenantId, x.GradeId },
                        principalTable: "Grades",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_HoldingCompanies~",
                        columns: x => new { x.TenantId, x.HoldingCompanyId },
                        principalTable: "HoldingCompanies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_LinesOfBusiness_~",
                        columns: x => new { x.TenantId, x.LobId },
                        principalTable: "LinesOfBusiness",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Organisations_Te~",
                        columns: x => new { x.TenantId, x.OrganisationId },
                        principalTable: "Organisations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_Sections_TenantI~",
                        columns: x => new { x.TenantId, x.SectionId },
                        principalTable: "Sections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_SubDepartments_T~",
                        columns: x => new { x.TenantId, x.SubDepartmentId },
                        principalTable: "SubDepartments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_SubFunctions_Ten~",
                        columns: x => new { x.TenantId, x.SubFunctionId },
                        principalTable: "SubFunctions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_SubSections_Tena~",
                        columns: x => new { x.TenantId, x.SubSectionId },
                        principalTable: "SubSections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionApplicabilitySets_WorkLocations_Te~",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationReviewQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationReviewQuestionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReviewQuestionOptions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReviewQuestionOptions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReviewQuestionOptions_ConfirmationReviewQuestion~",
                        columns: x => new { x.TenantId, x.ConfirmationReviewQuestionId },
                        principalTable: "ConfirmationReviewQuestions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationRatingScales_TenantId_Code",
                table: "ConfirmationRatingScales",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationRatingScaleValues_TenantId_ConfirmationRatingSc~1",
                table: "ConfirmationRatingScaleValues",
                columns: new[] { "TenantId", "ConfirmationRatingScaleId", "RatingValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationRatingScaleValues_TenantId_ConfirmationRatingSca~",
                table: "ConfirmationRatingScaleValues",
                columns: new[] { "TenantId", "ConfirmationRatingScaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_CountryLocationId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                column: "CountryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Confir~1",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "ConfirmationReviewQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Confirm~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "ConfirmationPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_CostCen~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Departm~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Designa~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "DesignationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Employe~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "EmployeeTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Functio~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "FunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_GradeId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "GradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Holding~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "HoldingCompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_LobId",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "LobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Organis~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "OrganisationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_Section~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_SubDepa~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SubDepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_SubFunc~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SubFunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_SubSect~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "SubSectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionApplicabilitySets_TenantId_WorkLoc~",
                table: "ConfirmationReviewQuestionApplicabilitySets",
                columns: new[] { "TenantId", "WorkLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionOptions_TenantId_ConfirmationRevi~1",
                table: "ConfirmationReviewQuestionOptions",
                columns: new[] { "TenantId", "ConfirmationReviewQuestionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestionOptions_TenantId_ConfirmationRevie~",
                table: "ConfirmationReviewQuestionOptions",
                columns: new[] { "TenantId", "ConfirmationReviewQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestions_TenantId_Code",
                table: "ConfirmationReviewQuestions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReviewQuestions_TenantId_ConfirmationRatingScale~",
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
