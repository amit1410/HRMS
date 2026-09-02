using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmploymentDetailsAndPositionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Employees_TenantId_EmployeeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "DepartmentCode",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "Function",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "HoldingCompany",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "LineOfBusiness",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "Organisation",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "SubDepartment",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "SubFunction",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "SubSection",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "WorkLocation",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "Employees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeTypeId",
                table: "Employees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CountryLocationId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeTypeId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FunctionId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GradeId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HoldingCompanyId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LobId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PositionChangeReasonId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SectionId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubDepartmentId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubFunctionId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubSectionId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkLocationId",
                table: "EmployeeEmploymentHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                    table.UniqueConstraint("AK_CostCenters_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CostCenters_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeEmployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstHiredDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DateOfJoining = table.Column<DateOnly>(type: "date", nullable: false),
                    GroupDateOfJoining = table.Column<DateOnly>(type: "date", nullable: true),
                    ConfirmationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    JobStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProbationPeriod = table.Column<int>(type: "int", nullable: true),
                    ProbationPeriodUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReferredByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NoticePeriod = table.Column<int>(type: "int", nullable: true),
                    NoticePeriodUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeEmployments", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeEmployments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeEmployments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeEmployments_Employees_TenantId_ReferredByEmployeeId",
                        columns: x => new { x.TenantId, x.ReferredByEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeEmployments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTypes", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeTypes_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeTypes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Functions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Functions", x => x.Id);
                    table.UniqueConstraint("AK_Functions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Functions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Grades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grades", x => x.Id);
                    table.UniqueConstraint("AK_Grades_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Grades_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HoldingCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingCompanies", x => x.Id);
                    table.UniqueConstraint("AK_HoldingCompanies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_HoldingCompanies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                    table.UniqueConstraint("AK_Organisations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Organisations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PositionChangeReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionChangeReasons", x => x.Id);
                    table.UniqueConstraint("AK_PositionChangeReasons_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PositionChangeReasons_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubDepartments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubDepartments", x => x.Id);
                    table.UniqueConstraint("AK_SubDepartments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SubDepartments_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubDepartments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLocations", x => x.Id);
                    table.UniqueConstraint("AK_WorkLocations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_WorkLocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubFunctions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    FunctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubFunctions", x => x.Id);
                    table.UniqueConstraint("AK_SubFunctions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SubFunctions_Functions_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "Functions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SubFunctions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LinesOfBusiness",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    HoldingCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinesOfBusiness", x => x.Id);
                    table.UniqueConstraint("AK_LinesOfBusiness_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LinesOfBusiness_HoldingCompanies_HoldingCompanyId",
                        column: x => x.HoldingCompanyId,
                        principalTable: "HoldingCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LinesOfBusiness_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SubDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Id);
                    table.UniqueConstraint("AK_Sections_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Sections_SubDepartments_SubDepartmentId",
                        column: x => x.SubDepartmentId,
                        principalTable: "SubDepartments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Sections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubSections", x => x.Id);
                    table.UniqueConstraint("AK_SubSections_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SubSections_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SubSections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CostCenterId",
                table: "Employees",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_EmployeeTypeId",
                table: "Employees",
                column: "EmployeeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_CostCenterId",
                table: "EmployeeEmploymentHistory",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_CountryLocationId",
                table: "EmployeeEmploymentHistory",
                column: "CountryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_DepartmentId",
                table: "EmployeeEmploymentHistory",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_DesignationId",
                table: "EmployeeEmploymentHistory",
                column: "DesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_EmployeeTypeId",
                table: "EmployeeEmploymentHistory",
                column: "EmployeeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_FunctionId",
                table: "EmployeeEmploymentHistory",
                column: "FunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_GradeId",
                table: "EmployeeEmploymentHistory",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_HoldingCompanyId",
                table: "EmployeeEmploymentHistory",
                column: "HoldingCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_LobId",
                table: "EmployeeEmploymentHistory",
                column: "LobId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_ManagerId",
                table: "EmployeeEmploymentHistory",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_OrganisationId",
                table: "EmployeeEmploymentHistory",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_PositionChangeReasonId",
                table: "EmployeeEmploymentHistory",
                column: "PositionChangeReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_SectionId",
                table: "EmployeeEmploymentHistory",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_SubDepartmentId",
                table: "EmployeeEmploymentHistory",
                column: "SubDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_SubFunctionId",
                table: "EmployeeEmploymentHistory",
                column: "SubFunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_SubSectionId",
                table: "EmployeeEmploymentHistory",
                column: "SubSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_WorkLocationId",
                table: "EmployeeEmploymentHistory",
                column: "WorkLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_TenantId_Code",
                table: "CostCenters",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_TenantId_Name",
                table: "CostCenters",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmployments_TenantId_EmployeeId",
                table: "EmployeeEmployments",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmployments_TenantId_ReferredByEmployeeId",
                table: "EmployeeEmployments",
                columns: new[] { "TenantId", "ReferredByEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTypes_TenantId_Code",
                table: "EmployeeTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTypes_TenantId_Name",
                table: "EmployeeTypes",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Functions_TenantId_Code",
                table: "Functions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Functions_TenantId_Name",
                table: "Functions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Grades_TenantId_Code",
                table: "Grades",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Grades_TenantId_Name",
                table: "Grades",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HoldingCompanies_TenantId_Code",
                table: "HoldingCompanies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HoldingCompanies_TenantId_Name",
                table: "HoldingCompanies",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinesOfBusiness_HoldingCompanyId",
                table: "LinesOfBusiness",
                column: "HoldingCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_LinesOfBusiness_TenantId_Code",
                table: "LinesOfBusiness",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinesOfBusiness_TenantId_Name",
                table: "LinesOfBusiness",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_TenantId_Code",
                table: "Organisations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_TenantId_Name",
                table: "Organisations",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PositionChangeReasons_TenantId_Code",
                table: "PositionChangeReasons",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PositionChangeReasons_TenantId_Name",
                table: "PositionChangeReasons",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_SubDepartmentId",
                table: "Sections",
                column: "SubDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_TenantId_Code",
                table: "Sections",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_TenantId_Name",
                table: "Sections",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubDepartments_TenantId_Code",
                table: "SubDepartments",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubDepartments_TenantId_DepartmentId",
                table: "SubDepartments",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubDepartments_TenantId_Name",
                table: "SubDepartments",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubFunctions_FunctionId",
                table: "SubFunctions",
                column: "FunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubFunctions_TenantId_Code",
                table: "SubFunctions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubFunctions_TenantId_Name",
                table: "SubFunctions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubSections_SectionId",
                table: "SubSections",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubSections_TenantId_Code",
                table: "SubSections",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubSections_TenantId_Name",
                table: "SubSections",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkLocations_TenantId_Code",
                table: "WorkLocations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkLocations_TenantId_Name",
                table: "WorkLocations",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_CostCenters_CostCenterId",
                table: "EmployeeEmploymentHistory",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Countries_CountryLocationId",
                table: "EmployeeEmploymentHistory",
                column: "CountryLocationId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Departments_DepartmentId",
                table: "EmployeeEmploymentHistory",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Designations_DesignationId",
                table: "EmployeeEmploymentHistory",
                column: "DesignationId",
                principalTable: "Designations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_EmployeeTypes_EmployeeTypeId",
                table: "EmployeeEmploymentHistory",
                column: "EmployeeTypeId",
                principalTable: "EmployeeTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Employees_ManagerId",
                table: "EmployeeEmploymentHistory",
                column: "ManagerId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Employees_TenantId_EmployeeId",
                table: "EmployeeEmploymentHistory",
                columns: new[] { "TenantId", "EmployeeId" },
                principalTable: "Employees",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Functions_FunctionId",
                table: "EmployeeEmploymentHistory",
                column: "FunctionId",
                principalTable: "Functions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Grades_GradeId",
                table: "EmployeeEmploymentHistory",
                column: "GradeId",
                principalTable: "Grades",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_HoldingCompanies_HoldingCompanyId",
                table: "EmployeeEmploymentHistory",
                column: "HoldingCompanyId",
                principalTable: "HoldingCompanies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_LinesOfBusiness_LobId",
                table: "EmployeeEmploymentHistory",
                column: "LobId",
                principalTable: "LinesOfBusiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Organisations_OrganisationId",
                table: "EmployeeEmploymentHistory",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_PositionChangeReasons_PositionChangeReasonId",
                table: "EmployeeEmploymentHistory",
                column: "PositionChangeReasonId",
                principalTable: "PositionChangeReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Sections_SectionId",
                table: "EmployeeEmploymentHistory",
                column: "SectionId",
                principalTable: "Sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_SubDepartments_SubDepartmentId",
                table: "EmployeeEmploymentHistory",
                column: "SubDepartmentId",
                principalTable: "SubDepartments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_SubFunctions_SubFunctionId",
                table: "EmployeeEmploymentHistory",
                column: "SubFunctionId",
                principalTable: "SubFunctions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_SubSections_SubSectionId",
                table: "EmployeeEmploymentHistory",
                column: "SubSectionId",
                principalTable: "SubSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_WorkLocations_WorkLocationId",
                table: "EmployeeEmploymentHistory",
                column: "WorkLocationId",
                principalTable: "WorkLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_CostCenters_CostCenterId",
                table: "Employees",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_EmployeeTypes_EmployeeTypeId",
                table: "Employees",
                column: "EmployeeTypeId",
                principalTable: "EmployeeTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_CostCenters_CostCenterId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Countries_CountryLocationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Departments_DepartmentId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Designations_DesignationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_EmployeeTypes_EmployeeTypeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Employees_ManagerId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Employees_TenantId_EmployeeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Functions_FunctionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Grades_GradeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_HoldingCompanies_HoldingCompanyId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_LinesOfBusiness_LobId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Organisations_OrganisationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_PositionChangeReasons_PositionChangeReasonId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_Sections_SectionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_SubDepartments_SubDepartmentId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_SubFunctions_SubFunctionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_SubSections_SubSectionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeEmploymentHistory_WorkLocations_WorkLocationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_CostCenters_CostCenterId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_EmployeeTypes_EmployeeTypeId",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "CostCenters");

            migrationBuilder.DropTable(
                name: "EmployeeEmployments");

            migrationBuilder.DropTable(
                name: "EmployeeTypes");

            migrationBuilder.DropTable(
                name: "Grades");

            migrationBuilder.DropTable(
                name: "LinesOfBusiness");

            migrationBuilder.DropTable(
                name: "Organisations");

            migrationBuilder.DropTable(
                name: "PositionChangeReasons");

            migrationBuilder.DropTable(
                name: "SubFunctions");

            migrationBuilder.DropTable(
                name: "SubSections");

            migrationBuilder.DropTable(
                name: "WorkLocations");

            migrationBuilder.DropTable(
                name: "HoldingCompanies");

            migrationBuilder.DropTable(
                name: "Functions");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "SubDepartments");

            migrationBuilder.DropIndex(
                name: "IX_Employees_CostCenterId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_EmployeeTypeId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_CostCenterId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_CountryLocationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_DepartmentId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_DesignationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_EmployeeTypeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_FunctionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_GradeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_HoldingCompanyId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_LobId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_ManagerId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_OrganisationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_PositionChangeReasonId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_SectionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_SubDepartmentId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_SubFunctionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_SubSectionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentHistory_WorkLocationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmployeeTypeId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "CountryLocationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "EmployeeTypeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "FunctionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "GradeId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "HoldingCompanyId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "LobId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "PositionChangeReasonId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "SubDepartmentId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "SubFunctionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "SubSectionId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.DropColumn(
                name: "WorkLocationId",
                table: "EmployeeEmploymentHistory");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentCode",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Function",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Grade",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldingCompany",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineOfBusiness",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Organisation",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubDepartment",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubFunction",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubSection",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkLocation",
                table: "EmployeeEmploymentHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeEmploymentHistory_Employees_TenantId_EmployeeId",
                table: "EmployeeEmploymentHistory",
                columns: new[] { "TenantId", "EmployeeId" },
                principalTable: "Employees",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
