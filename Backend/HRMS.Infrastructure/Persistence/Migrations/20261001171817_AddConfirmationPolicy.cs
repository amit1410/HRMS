using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfirmationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationPolicies", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationPolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationPolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationPolicyVersions", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationPolicyVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyVersions_ConfirmationPolicies_TenantId_ConfirmationPolicyId",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyId },
                        principalTable: "ConfirmationPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationPolicyApplicabilitySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_ConfirmationPolicyApplicabilitySets", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationPolicyApplicabilitySets_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_ConfirmationPolicyVersions_TenantId_ConfirmationPolicyVersionId",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyVersionId },
                        principalTable: "ConfirmationPolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_CostCenters_TenantId_CostCenterId",
                        columns: x => new { x.TenantId, x.CostCenterId },
                        principalTable: "CostCenters",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_Countries_CountryLocationId",
                        column: x => x.CountryLocationId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_Designations_TenantId_DesignationId",
                        columns: x => new { x.TenantId, x.DesignationId },
                        principalTable: "Designations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_EmployeeTypes_TenantId_EmployeeTypeId",
                        columns: x => new { x.TenantId, x.EmployeeTypeId },
                        principalTable: "EmployeeTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_Functions_TenantId_FunctionId",
                        columns: x => new { x.TenantId, x.FunctionId },
                        principalTable: "Functions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_Grades_TenantId_GradeId",
                        columns: x => new { x.TenantId, x.GradeId },
                        principalTable: "Grades",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_HoldingCompanies_TenantId_HoldingCompanyId",
                        columns: x => new { x.TenantId, x.HoldingCompanyId },
                        principalTable: "HoldingCompanies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_LinesOfBusiness_TenantId_LobId",
                        columns: x => new { x.TenantId, x.LobId },
                        principalTable: "LinesOfBusiness",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_Organisations_TenantId_OrganisationId",
                        columns: x => new { x.TenantId, x.OrganisationId },
                        principalTable: "Organisations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_Sections_TenantId_SectionId",
                        columns: x => new { x.TenantId, x.SectionId },
                        principalTable: "Sections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_SubDepartments_TenantId_SubDepartmentId",
                        columns: x => new { x.TenantId, x.SubDepartmentId },
                        principalTable: "SubDepartments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_SubFunctions_TenantId_SubFunctionId",
                        columns: x => new { x.TenantId, x.SubFunctionId },
                        principalTable: "SubFunctions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_SubSections_TenantId_SubSectionId",
                        columns: x => new { x.TenantId, x.SubSectionId },
                        principalTable: "SubSections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyApplicabilitySets_WorkLocations_TenantId_WorkLocationId",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfirmationPolicyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProbationDuration = table.Column<int>(type: "int", nullable: false),
                    ProbationDurationUnit = table.Column<int>(type: "int", nullable: false),
                    TriggerDaysBeforeEnd = table.Column<int>(type: "int", nullable: false),
                    MaxExtensions = table.Column<int>(type: "int", nullable: false),
                    ExtensionDuration = table.Column<int>(type: "int", nullable: false),
                    ExtensionDurationUnit = table.Column<int>(type: "int", nullable: false),
                    RequiredApproval = table.Column<int>(type: "int", nullable: false),
                    ConfirmRatingThreshold = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    AutoConfirmOnApproval = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationPolicyRules", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationPolicyRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationPolicyRules_ConfirmationPolicyVersions_TenantId_ConfirmationPolicyVersionId",
                        columns: x => new { x.TenantId, x.ConfirmationPolicyVersionId },
                        principalTable: "ConfirmationPolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicies_TenantId_Code",
                table: "ConfirmationPolicies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_CountryLocationId",
                table: "ConfirmationPolicyApplicabilitySets",
                column: "CountryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_ConfirmationPolicyVersionId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "ConfirmationPolicyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_CostCenterId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_DepartmentId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_DesignationId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "DesignationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_EmployeeTypeId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "EmployeeTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_FunctionId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "FunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_GradeId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "GradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_HoldingCompanyId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "HoldingCompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_LobId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "LobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_OrganisationId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "OrganisationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_SectionId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_SubDepartmentId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "SubDepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_SubFunctionId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "SubFunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_SubSectionId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "SubSectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyApplicabilitySets_TenantId_WorkLocationId",
                table: "ConfirmationPolicyApplicabilitySets",
                columns: new[] { "TenantId", "WorkLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyRules_TenantId_ConfirmationPolicyVersionId",
                table: "ConfirmationPolicyRules",
                columns: new[] { "TenantId", "ConfirmationPolicyVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyVersions_TenantId_ConfirmationPolicyId_VersionNumber",
                table: "ConfirmationPolicyVersions",
                columns: new[] { "TenantId", "ConfirmationPolicyId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationPolicyVersions_TenantId_Status_EffectiveFrom_EffectiveTo",
                table: "ConfirmationPolicyVersions",
                columns: new[] { "TenantId", "Status", "EffectiveFrom", "EffectiveTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfirmationPolicyApplicabilitySets");

            migrationBuilder.DropTable(
                name: "ConfirmationPolicyRules");

            migrationBuilder.DropTable(
                name: "ConfirmationPolicyVersions");

            migrationBuilder.DropTable(
                name: "ConfirmationPolicies");
        }
    }
}
