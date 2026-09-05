using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeavePolicyFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeavePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePeriods", x => x.Id);
                    table.UniqueConstraint("AK_LeavePeriods_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePeriods_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicies",
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
                    table.PrimaryKey("PK_LeavePolicies", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultUnit = table.Column<int>(type: "int", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                    table.UniqueConstraint("AK_LeaveTypes_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveTypes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_LeavePolicyVersions", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyVersions_LeavePolicies_TenantId_LeavePolicyId",
                        columns: x => new { x.TenantId, x.LeavePolicyId },
                        principalTable: "LeavePolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicyApplicabilitySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_LeavePolicyApplicabilitySets", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyApplicabilitySets_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_CostCenters_TenantId_CostCenterId",
                        columns: x => new { x.TenantId, x.CostCenterId },
                        principalTable: "CostCenters",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Countries_CountryLocationId",
                        column: x => x.CountryLocationId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Designations_TenantId_DesignationId",
                        columns: x => new { x.TenantId, x.DesignationId },
                        principalTable: "Designations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_EmployeeTypes_TenantId_EmployeeTypeId",
                        columns: x => new { x.TenantId, x.EmployeeTypeId },
                        principalTable: "EmployeeTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Functions_TenantId_FunctionId",
                        columns: x => new { x.TenantId, x.FunctionId },
                        principalTable: "Functions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Grades_TenantId_GradeId",
                        columns: x => new { x.TenantId, x.GradeId },
                        principalTable: "Grades",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_HoldingCompanies_TenantId_HoldingCompanyId",
                        columns: x => new { x.TenantId, x.HoldingCompanyId },
                        principalTable: "HoldingCompanies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_LeavePolicyVersions_TenantId_LeavePolicyVersionId",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId },
                        principalTable: "LeavePolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_LinesOfBusiness_TenantId_LobId",
                        columns: x => new { x.TenantId, x.LobId },
                        principalTable: "LinesOfBusiness",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Organisations_TenantId_OrganisationId",
                        columns: x => new { x.TenantId, x.OrganisationId },
                        principalTable: "Organisations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Sections_TenantId_SectionId",
                        columns: x => new { x.TenantId, x.SectionId },
                        principalTable: "Sections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_SubDepartments_TenantId_SubDepartmentId",
                        columns: x => new { x.TenantId, x.SubDepartmentId },
                        principalTable: "SubDepartments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_SubFunctions_TenantId_SubFunctionId",
                        columns: x => new { x.TenantId, x.SubFunctionId },
                        principalTable: "SubFunctions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_SubSections_TenantId_SubSectionId",
                        columns: x => new { x.TenantId, x.SubSectionId },
                        principalTable: "SubSections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_WorkLocations_TenantId_WorkLocationId",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyRules_LeavePolicyVersions_TenantId_LeavePolicyVersionId",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId },
                        principalTable: "LeavePolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyRules_LeaveTypes_TenantId_LeaveTypeId",
                        columns: x => new { x.TenantId, x.LeaveTypeId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePeriods_TenantId_Code",
                table: "LeavePeriods",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_TenantId_Code",
                table: "LeavePolicies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_CountryLocationId",
                table: "LeavePolicyApplicabilitySets",
                column: "CountryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_CostCenterId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_DepartmentId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_DesignationId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "DesignationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_EmployeeTypeId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "EmployeeTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_FunctionId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "FunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_GradeId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "GradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_HoldingCompanyId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "HoldingCompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_LeavePolicyVersionId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "LeavePolicyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_LobId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "LobId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_OrganisationId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "OrganisationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_SectionId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_SubDepartmentId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "SubDepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_SubFunctionId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "SubFunctionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_SubSectionId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "SubSectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyApplicabilitySets_TenantId_WorkLocationId",
                table: "LeavePolicyApplicabilitySets",
                columns: new[] { "TenantId", "WorkLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyRules_TenantId_LeavePolicyVersionId_LeaveTypeId",
                table: "LeavePolicyRules",
                columns: new[] { "TenantId", "LeavePolicyVersionId", "LeaveTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyRules_TenantId_LeaveTypeId",
                table: "LeavePolicyRules",
                columns: new[] { "TenantId", "LeaveTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyVersions_TenantId_LeavePolicyId_VersionNumber",
                table: "LeavePolicyVersions",
                columns: new[] { "TenantId", "LeavePolicyId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyVersions_TenantId_Status_EffectiveFrom_EffectiveTo",
                table: "LeavePolicyVersions",
                columns: new[] { "TenantId", "Status", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_TenantId_Code",
                table: "LeaveTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeavePeriods");

            migrationBuilder.DropTable(
                name: "LeavePolicyApplicabilitySets");

            migrationBuilder.DropTable(
                name: "LeavePolicyRules");

            migrationBuilder.DropTable(
                name: "LeavePolicyVersions");

            migrationBuilder.DropTable(
                name: "LeaveTypes");

            migrationBuilder.DropTable(
                name: "LeavePolicies");
        }
    }
}
