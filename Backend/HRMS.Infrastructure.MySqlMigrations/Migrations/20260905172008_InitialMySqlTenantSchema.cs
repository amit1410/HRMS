using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialMySqlTenantSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "varchar(253)", maxLength: 253, nullable: false),
                    ShardKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "States",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CountryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_States", x => x.Id);
                    table.ForeignKey(
                        name: "FK_States_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Banks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id);
                    table.UniqueConstraint("AK_Banks_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Banks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.UniqueConstraint("AK_Departments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Departments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Designations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Id);
                    table.UniqueConstraint("AK_Designations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Designations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeCodeConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AutoGenerate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AssignmentMode = table.Column<int>(type: "int", nullable: false),
                    GenerationMethod = table.Column<int>(type: "int", nullable: true),
                    Prefix = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Padding = table.Column<int>(type: "int", nullable: false),
                    Separator = table.Column<string>(type: "varchar(1)", maxLength: 1, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeConfigs", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeCodeConfigs_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeCodeConfigs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Functions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Grades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HoldingCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FileName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ImportedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    SuccessfulRows = table.Column<int>(type: "int", nullable: false),
                    FailedRows = table.Column<int>(type: "int", nullable: false),
                    SkippedRows = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    DefaultUnit = table.Column<int>(type: "int", nullable: false),
                    IsPaid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PositionChangeReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    LastLoginDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.UniqueConstraint("AK_Users_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WorkLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    StateId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cities_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubDepartments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    DepartmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeCodeConfigVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCodeConfigId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AutoGenerate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AssignmentMode = table.Column<int>(type: "int", nullable: false),
                    GenerationMethod = table.Column<int>(type: "int", nullable: true),
                    Prefix = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Separator = table.Column<string>(type: "varchar(1)", maxLength: 1, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Padding = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeConfigVersions", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeCodeConfigVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeCodeConfigVersions_EmployeeCodeConfigs_TenantId_Empl~",
                        columns: x => new { x.TenantId, x.EmployeeCodeConfigId },
                        principalTable: "EmployeeCodeConfigs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeConfigVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubFunctions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    FunctionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LinesOfBusiness",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    HoldingCompanyId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Salutation = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    BloodGroup = table.Column<int>(type: "int", nullable: false),
                    MaritalStatus = table.Column<int>(type: "int", nullable: false),
                    BirthCountry = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    BirthState = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    BirthCity = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    BirthCountryId = table.Column<Guid>(type: "char(36)", nullable: true),
                    BirthStateId = table.Column<Guid>(type: "char(36)", nullable: true),
                    BirthCityId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Religion = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Caste = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DateOfJoining = table.Column<DateOnly>(type: "date", nullable: false),
                    GroupDateOfJoining = table.Column<DateOnly>(type: "date", nullable: true),
                    DateOfLeaving = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JobStatus = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    GroupId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    DepartmentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DesignationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ReportingManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Address = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    AadhaarNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    PanNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    PfNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    UanNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    EsicNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    MediclaimNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Gratuity = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Pension = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CostCenterCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    CostCenterId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PayrollLocation = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    EsicApplicable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Citizenship = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    LanguageKnown = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ProfilePictureUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    EmployeeType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    EmployeeTypeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ManagerCategories = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.UniqueConstraint("AK_Employees_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Employees_Cities_BirthCityId",
                        column: x => x.BirthCityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Employees_Countries_BirthCountryId",
                        column: x => x.BirthCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Designations_TenantId_DesignationId",
                        columns: x => new { x.TenantId, x.DesignationId },
                        principalTable: "Designations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_EmployeeTypes_EmployeeTypeId",
                        column: x => x.EmployeeTypeId,
                        principalTable: "EmployeeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Employees_Employees_TenantId_ReportingManagerId",
                        columns: x => new { x.TenantId, x.ReportingManagerId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_States_BirthStateId",
                        column: x => x.BirthStateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    SubDepartmentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeCodeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCodeConfigId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCodeConfigVersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeRules", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeCodeRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRules_EmployeeCodeConfigVersions_TenantId_Employ~",
                        columns: x => new { x.TenantId, x.EmployeeCodeConfigVersionId },
                        principalTable: "EmployeeCodeConfigVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRules_EmployeeCodeConfigs_TenantId_EmployeeCodeC~",
                        columns: x => new { x.TenantId, x.EmployeeCodeConfigId },
                        principalTable: "EmployeeCodeConfigs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.UniqueConstraint("AK_LeavePolicyRules_TenantId_LeavePolicyVersionId_Id", x => new { x.TenantId, x.LeavePolicyVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyRules_LeavePolicyVersions_TenantId_LeavePolicyVer~",
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AccountEmployeeLinkEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Operation = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    PreviousEventId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PreviousLinkId = table.Column<Guid>(type: "char(36)", nullable: true),
                    NewLinkId = table.Column<Guid>(type: "char(36)", nullable: true),
                    BeforeEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    AfterEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmployeeLinkEvents", x => x.Id);
                    table.UniqueConstraint("AK_AccountEmployeeLinkEvents_TenantId_SubjectUserId_Id", x => new { x.TenantId, x.SubjectUserId, x.Id });
                    table.CheckConstraint("CK_AccountEmployeeLinkEvents_Shape", "`Sequence` > 0 AND `Operation` IN ('Link','Unlink','Replace') AND `Reason` <> '' AND `CorrelationId` <> '' AND ((`Operation` = 'Link' AND `PreviousLinkId` IS NULL AND `BeforeEmployeeId` IS NULL AND `NewLinkId` = `Id` AND `AfterEmployeeId` IS NOT NULL) OR (`Operation` = 'Unlink' AND `PreviousLinkId` IS NOT NULL AND `BeforeEmployeeId` IS NOT NULL AND `NewLinkId` IS NULL AND `AfterEmployeeId` IS NULL) OR (`Operation` = 'Replace' AND `PreviousLinkId` IS NOT NULL AND `BeforeEmployeeId` IS NOT NULL AND `NewLinkId` = `Id` AND `AfterEmployeeId` IS NOT NULL AND `BeforeEmployeeId` <> `AfterEmployeeId`))");
                    table.ForeignKey(
                        name: "FK_AELE_PreviousEvent",
                        columns: x => new { x.TenantId, x.SubjectUserId, x.PreviousEventId },
                        principalTable: "AccountEmployeeLinkEvents",
                        principalColumns: new[] { "TenantId", "SubjectUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AELE_PreviousLink",
                        columns: x => new { x.TenantId, x.SubjectUserId, x.PreviousLinkId },
                        principalTable: "AccountEmployeeLinkEvents",
                        principalColumns: new[] { "TenantId", "SubjectUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Employees_TenantId_AfterEmployeeId",
                        columns: x => new { x.TenantId, x.AfterEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Employees_TenantId_BeforeEmployeeId",
                        columns: x => new { x.TenantId, x.BeforeEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Users_TenantId_SubjectUserId",
                        columns: x => new { x.TenantId, x.SubjectUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeAdditionalInfo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Division = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    PaPsa = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    AdditionalEmployeeCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    ContractId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAdditionalInfo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeAdditionalInfo_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeAdditionalInfo_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AddressType = table.Column<int>(type: "int", nullable: false),
                    Country = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ZipCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    AddressLine1 = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    AddressLine2 = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    HouseNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeAddresses_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeAddresses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Module = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Section = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    EntityName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    RecordId = table.Column<Guid>(type: "char(36)", nullable: true),
                    FieldName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    OldValue = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChangedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    ImportBatchId = table.Column<Guid>(type: "char(36)", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeAuditLogs_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeAuditLogs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeBankDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    BankId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AccountHolderName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    AccountNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    AccountPurpose = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IfscCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    BranchName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    DocumentOfProof = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeBankDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeBankDetails_Banks_TenantId_BankId",
                        columns: x => new { x.TenantId, x.BankId },
                        principalTable: "Banks",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeBankDetails_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeBankDetails_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OfficialEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    PersonalEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    AlternateEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    OfficialPhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    PersonalPhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    EmergencyNumber = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    SameAsCurrentAddress = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeContacts_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeContacts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeEducationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EducationLevel = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Qualification = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    University = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Institute = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    EducationType = table.Column<int>(type: "int", nullable: false),
                    AreaOfSpecialization = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    YearOfPassing = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    DocumentOfProof = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeEducationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeEducationRecords_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeEducationRecords_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeEmployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FirstHiredDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DateOfJoining = table.Column<DateOnly>(type: "date", nullable: false),
                    GroupDateOfJoining = table.Column<DateOnly>(type: "date", nullable: true),
                    ConfirmationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    JobStatus = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ProbationPeriod = table.Column<int>(type: "int", nullable: true),
                    ProbationPeriodUnit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    ReferredByEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    NoticePeriod = table.Column<int>(type: "int", nullable: true),
                    NoticePeriodUnit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeFamilyMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Salutation = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Relationship = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    BloodGroup = table.Column<int>(type: "int", nullable: false),
                    Nationality = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Occupation = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    IsNominee = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDependent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NomineePercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeFamilyMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeFamilyMembers_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeFamilyMembers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeLeaveBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GrantedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLeaveBalances", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeLeaveBalances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_EmployeeLeaveBalances_NonNegativeAndAvailable", "`GrantedQuantity` >= 0 AND `ReservedQuantity` >= 0 AND `ConsumedQuantity` >= 0 AND `ReservedQuantity` + `ConsumedQuantity` <= `GrantedQuantity`");
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveBalances_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveBalances_LeavePeriods_TenantId_LeavePeriodId",
                        columns: x => new { x.TenantId, x.LeavePeriodId },
                        principalTable: "LeavePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveBalances_LeaveTypes_TenantId_LeaveTypeId",
                        columns: x => new { x.TenantId, x.LeaveTypeId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveBalances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeePreviousEmployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Company = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Designation = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    EmploymentType = table.Column<int>(type: "int", nullable: false),
                    TenureFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    TenureTill = table.Column<DateOnly>(type: "date", nullable: true),
                    DocumentOfProof = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePreviousEmployments", x => x.Id);
                    table.UniqueConstraint("AK_EmployeePreviousEmployments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeePreviousEmployments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeePreviousEmployments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeSupervisors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    L1ManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    L1ManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    L1ManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    L2ManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    L2ManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    L2ManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    L3ManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    L3ManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    L3ManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    L4ManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    L4ManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    L4ManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    L5ManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    L5ManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    L5ManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    TimeManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    TimeManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    TimeManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EroCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    EroName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    EroId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ChroManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    ChroManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ChroManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSupervisors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeSupervisors_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeSupervisors_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeCodeRuleConditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCodeRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Field = table.Column<int>(type: "int", nullable: false),
                    Operator = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Value = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeRuleConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRuleConditions_EmployeeCodeRules_TenantId_Employ~",
                        columns: x => new { x.TenantId, x.EmployeeCodeRuleId },
                        principalTable: "EmployeeCodeRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRuleConditions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeCodeSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCodeRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SequenceOrder = table.Column<int>(type: "int", nullable: false),
                    SegmentType = table.Column<int>(type: "int", nullable: false),
                    FixedValue = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    PaddingLength = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSegments_EmployeeCodeRules_TenantId_EmployeeCode~",
                        columns: x => new { x.TenantId, x.EmployeeCodeRuleId },
                        principalTable: "EmployeeCodeRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSegments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeCodeSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCodeRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ScopeKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    IncrementBy = table.Column<int>(type: "int", nullable: false),
                    ResetPeriod = table.Column<int>(type: "int", nullable: false),
                    PeriodKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSequences_EmployeeCodeRules_TenantId_EmployeeCod~",
                        columns: x => new { x.TenantId, x.EmployeeCodeRuleId },
                        principalTable: "EmployeeCodeRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSequences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyAttachmentRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttachmentRequirement = table.Column<int>(type: "int", nullable: false),
                    ThresholdQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    DocumentLabel = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyAttachmentRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyAttachmentRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyAttachmentRules_LeavePolicyRules_TenantId_LeavePo~",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyCalendarRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    HolidayTreatment = table.Column<int>(type: "int", nullable: false),
                    WeekOffTreatment = table.Column<int>(type: "int", nullable: false),
                    SandwichMode = table.Column<int>(type: "int", nullable: false),
                    ApplyToPrefix = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ApplyToSuffix = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ApplyToBetween = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyCalendarRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyCalendarRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyCalendarRules_LeavePolicyRules_TenantId_LeavePoli~",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyCancellationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    WithdrawAllowed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CancelAllowed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ModifyAllowed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyCancellationRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyCancellationRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyCancellationRules_LeavePolicyRules_TenantId_Leave~",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyClubbingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LowerLeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    HigherLeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Relation = table.Column<int>(type: "int", nullable: false),
                    NormalizedPairKey = table.Column<string>(type: "varchar(73)", maxLength: 73, nullable: true, computedColumnSql: "CASE WHEN CAST(`LowerLeavePolicyRuleId` AS CHAR(36)) < CAST(`HigherLeavePolicyRuleId` AS CHAR(36)) THEN CONCAT(CAST(`LowerLeavePolicyRuleId` AS CHAR(36)), ':', CAST(`HigherLeavePolicyRuleId` AS CHAR(36))) ELSE CONCAT(CAST(`HigherLeavePolicyRuleId` AS CHAR(36)), ':', CAST(`LowerLeavePolicyRuleId` AS CHAR(36))) END", stored: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyClubbingRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyClubbingRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LeavePolicyClubbingRules_DifferentParticipants", "`LowerLeavePolicyRuleId` <> `HigherLeavePolicyRuleId`");
                    table.ForeignKey(
                        name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LeavePoli~",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId, x.HigherLeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "LeavePolicyVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyClubbingRules_LeavePolicyRules_TenantId_LeavePol~1",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId, x.LowerLeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "LeavePolicyVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyClubbingRules_LeavePolicyVersions_TenantId_LeaveP~",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId },
                        principalTable: "LeavePolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyEligibilityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EligibilityMode = table.Column<int>(type: "int", nullable: false),
                    MinimumServiceValue = table.Column<int>(type: "int", nullable: true),
                    MinimumServiceUnit = table.Column<int>(type: "int", nullable: true),
                    ProbationMode = table.Column<int>(type: "int", nullable: false),
                    NoticePeriodMode = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyEligibilityRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyEligibilityRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyEligibilityRules_LeavePolicyRules_TenantId_LeaveP~",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyEntitlementRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EntitlementMode = table.Column<int>(type: "int", nullable: false),
                    EntitlementSource = table.Column<int>(type: "int", nullable: false),
                    EntitlementQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    AccrualFrequency = table.Column<int>(type: "int", nullable: false),
                    AccrualTiming = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyEntitlementRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyEntitlementRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyEntitlementRules_LeavePolicyRules_TenantId_LeaveP~",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyRequestRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    MinimumRequestQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    MaximumRequestQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    MaximumConsecutiveQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    MinimumAdvanceNoticeDays = table.Column<int>(type: "int", nullable: false),
                    BackdatedRequestMode = table.Column<int>(type: "int", nullable: false),
                    MaximumBackdatedDays = table.Column<int>(type: "int", nullable: true),
                    MaximumRequestsPerPeriod = table.Column<int>(type: "int", nullable: true),
                    MaximumQuantityPerPeriod = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    RequestLimitPeriod = table.Column<int>(type: "int", nullable: true),
                    PartialDayMode = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyRequestRules", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyRequestRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyRequestRules_LeavePolicyRules_TenantId_LeavePolic~",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AccountEmployeeCurrentLinks",
                columns: table => new
                {
                    LinkId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmployeeCurrentLinks", x => x.LinkId);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_AccountEmployeeLinkEvents_Tenant~",
                        columns: x => new { x.TenantId, x.UserId, x.LinkId },
                        principalTable: "AccountEmployeeLinkEvents",
                        principalColumns: new[] { "TenantId", "SubjectUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PreviousEmploymentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DocumentName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    DocumentCategory = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    FilePath = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    UploadedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeDocuments_EmployeePreviousEmployments_TenantId_Previ~",
                        columns: x => new { x.TenantId, x.PreviousEmploymentId },
                        principalTable: "EmployeePreviousEmployments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeDocuments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeDocuments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeEmploymentHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
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
                    ManagerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PositionChangeReasonId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ChangeReason = table.Column<int>(type: "int", nullable: false),
                    ChangeReasonDescription = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    BusinessRole = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    GradeLevel = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    CareerGroup = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    EmploymentType = table.Column<int>(type: "int", nullable: false),
                    EmploymentStatus = table.Column<int>(type: "int", nullable: false),
                    DesignationName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    DepartmentName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ManagerCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    ManagerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeEmploymentHistory", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeEmploymentHistory_TenantId_EmployeeId_Id", x => new { x.TenantId, x.EmployeeId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Countries_CountryLocationId",
                        column: x => x.CountryLocationId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Designations_DesignationId",
                        column: x => x.DesignationId,
                        principalTable: "Designations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_EmployeeTypes_EmployeeTypeId",
                        column: x => x.EmployeeTypeId,
                        principalTable: "EmployeeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Employees_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Functions_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "Functions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_HoldingCompanies_HoldingCompanyId",
                        column: x => x.HoldingCompanyId,
                        principalTable: "HoldingCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_LinesOfBusiness_LobId",
                        column: x => x.LobId,
                        principalTable: "LinesOfBusiness",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_PositionChangeReasons_PositionChan~",
                        column: x => x.PositionChangeReasonId,
                        principalTable: "PositionChangeReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_SubDepartments_SubDepartmentId",
                        column: x => x.SubDepartmentId,
                        principalTable: "SubDepartments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_SubFunctions_SubFunctionId",
                        column: x => x.SubFunctionId,
                        principalTable: "SubFunctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_SubSections_SubSectionId",
                        column: x => x.SubSectionId,
                        principalTable: "SubSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentHistory_WorkLocations_WorkLocationId",
                        column: x => x.WorkLocationId,
                        principalTable: "WorkLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeavePolicyApplicabilitySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
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
                    table.PrimaryKey("PK_LeavePolicyApplicabilitySets", x => x.Id);
                    table.UniqueConstraint("AK_LeavePolicyApplicabilitySets_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_CostCenters_TenantId_CostCenter~",
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
                        name: "FK_LeavePolicyApplicabilitySets_Departments_TenantId_Department~",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_Designations_TenantId_Designati~",
                        columns: x => new { x.TenantId, x.DesignationId },
                        principalTable: "Designations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_EmployeeTypes_TenantId_Employee~",
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
                        name: "FK_LeavePolicyApplicabilitySets_HoldingCompanies_TenantId_Holdi~",
                        columns: x => new { x.TenantId, x.HoldingCompanyId },
                        principalTable: "HoldingCompanies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_LeavePolicyVersions_TenantId_Le~",
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
                        name: "FK_LeavePolicyApplicabilitySets_Organisations_TenantId_Organisa~",
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
                        name: "FK_LeavePolicyApplicabilitySets_SubDepartments_TenantId_SubDepa~",
                        columns: x => new { x.TenantId, x.SubDepartmentId },
                        principalTable: "SubDepartments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_SubFunctions_TenantId_SubFuncti~",
                        columns: x => new { x.TenantId, x.SubFunctionId },
                        principalTable: "SubFunctions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_SubSections_TenantId_SubSection~",
                        columns: x => new { x.TenantId, x.SubSectionId },
                        principalTable: "SubSections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeavePolicyApplicabilitySets_WorkLocations_TenantId_WorkLoca~",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeEmploymentHistoryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PolicyGenderSnapshot = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ChargeableQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.UniqueConstraint("AK_LeaveRequests_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LeaveRequests_DateAndQuantity", "`StartDate` <= `EndDate` AND `RequestedQuantity` >= 0 AND `ChargeableQuantity` >= 0");
                    table.ForeignKey(
                        name: "FK_LeaveRequests_EmployeeEmploymentHistory_TenantId_EmployeeId_~",
                        columns: x => new { x.TenantId, x.EmployeeId, x.EmployeeEmploymentHistoryId },
                        principalTable: "EmployeeEmploymentHistory",
                        principalColumns: new[] { "TenantId", "EmployeeId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeavePeriods_TenantId_LeavePeriodId",
                        columns: x => new { x.TenantId, x.LeavePeriodId },
                        principalTable: "LeavePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeavePolicyRules_TenantId_LeavePolicyVersionId~",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "LeavePolicyVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeavePolicyVersions_TenantId_LeavePolicyVersio~",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId },
                        principalTable: "LeavePolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_TenantId_LeaveTypeId",
                        columns: x => new { x.TenantId, x.LeaveTypeId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveBalanceTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeLeaveBalanceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "char(36)", nullable: true),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceReference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ActorType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ActorEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveBalanceTransactions", x => x.Id);
                    table.UniqueConstraint("AK_LeaveBalanceTransactions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LeaveBalanceTransactions_PositiveQuantity", "`Quantity` > 0");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_EmployeeLeaveBalances_TenantId_Empl~",
                        columns: x => new { x.TenantId, x.EmployeeLeaveBalanceId },
                        principalTable: "EmployeeLeaveBalances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_Employees_TenantId_ActorEmployeeId",
                        columns: x => new { x.TenantId, x.ActorEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_LeavePeriods_TenantId_LeavePeriodId",
                        columns: x => new { x.TenantId, x.LeavePeriodId },
                        principalTable: "LeavePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_LeavePolicyRules_TenantId_LeavePoli~",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_LeavePolicyVersions_TenantId_LeaveP~",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId },
                        principalTable: "LeavePolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_LeaveRequests_TenantId_LeaveRequest~",
                        columns: x => new { x.TenantId, x.LeaveRequestId },
                        principalTable: "LeaveRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_LeaveTypes_TenantId_LeaveTypeId",
                        columns: x => new { x.TenantId, x.LeaveTypeId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceTransactions_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveRequestDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ChargeableQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    DayClassification = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    CalculationReason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    IsEmployeeRequested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestDays", x => x.Id);
                    table.UniqueConstraint("AK_LeaveRequestDays_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LeaveRequestDays_NonNegativeQuantity", "`RequestedQuantity` >= 0 AND `ChargeableQuantity` >= 0");
                    table.ForeignKey(
                        name: "FK_LeaveRequestDays_LeaveRequests_TenantId_LeaveRequestId",
                        columns: x => new { x.TenantId, x.LeaveRequestId },
                        principalTable: "LeaveRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestDays_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveRequestEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ActorType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ActorEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestEvents", x => x.Id);
                    table.UniqueConstraint("AK_LeaveRequestEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_Employees_TenantId_ActorEmployeeId",
                        columns: x => new { x.TenantId, x.ActorEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_LeaveRequests_TenantId_LeaveRequestId",
                        columns: x => new { x.TenantId, x.LeaveRequestId },
                        principalTable: "LeaveRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeCurrentLinks_TenantId_UserId_LinkId",
                table: "AccountEmployeeCurrentLinks",
                columns: new[] { "TenantId", "UserId", "LinkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccountEmployeeCurrentLinks_TenantId_EmployeeId",
                table: "AccountEmployeeCurrentLinks",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccountEmployeeCurrentLinks_TenantId_UserId",
                table: "AccountEmployeeCurrentLinks",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_ActorUserId",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_AfterEmployeeId_OccurredA~",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "AfterEmployeeId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_BeforeEmployeeId_Occurred~",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "BeforeEmployeeId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_SubjectUserId_PreviousEve~",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "SubjectUserId", "PreviousEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_SubjectUserId_PreviousLin~",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "SubjectUserId", "PreviousLinkId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_SubjectUserId_Sequence",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "SubjectUserId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Banks_TenantId_Code",
                table: "Banks",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Banks_TenantId_Name",
                table: "Banks",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_StateId_Code",
                table: "Cities",
                columns: new[] { "StateId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_StateId_Name",
                table: "Cities",
                columns: new[] { "StateId", "Name" },
                unique: true);

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
                name: "IX_Countries_Code",
                table: "Countries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Name",
                table: "Countries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId_Code",
                table: "Departments",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId_Name",
                table: "Departments",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Designations_TenantId_Code",
                table: "Designations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Designations_TenantId_Name",
                table: "Designations",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdditionalInfo_TenantId_EmployeeId",
                table: "EmployeeAdditionalInfo",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAddresses_TenantId_EmployeeId_AddressType",
                table: "EmployeeAddresses",
                columns: new[] { "TenantId", "EmployeeId", "AddressType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAuditLogs_TenantId_EmployeeId_CreatedDate",
                table: "EmployeeAuditLogs",
                columns: new[] { "TenantId", "EmployeeId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAuditLogs_TenantId_EmployeeId_Module",
                table: "EmployeeAuditLogs",
                columns: new[] { "TenantId", "EmployeeId", "Module" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAuditLogs_TenantId_ImportBatchId",
                table: "EmployeeAuditLogs",
                columns: new[] { "TenantId", "ImportBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBankDetails_TenantId_BankId",
                table: "EmployeeBankDetails",
                columns: new[] { "TenantId", "BankId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBankDetails_TenantId_EmployeeId",
                table: "EmployeeBankDetails",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeConfigVersions_TenantId_EmployeeCodeConfigId_Eff~",
                table: "EmployeeCodeConfigVersions",
                columns: new[] { "TenantId", "EmployeeCodeConfigId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeRuleConditions_TenantId_EmployeeCodeRuleId",
                table: "EmployeeCodeRuleConditions",
                columns: new[] { "TenantId", "EmployeeCodeRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeRules_TenantId_EmployeeCodeConfigId_Priority",
                table: "EmployeeCodeRules",
                columns: new[] { "TenantId", "EmployeeCodeConfigId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeRules_TenantId_EmployeeCodeConfigVersionId",
                table: "EmployeeCodeRules",
                columns: new[] { "TenantId", "EmployeeCodeConfigVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeSegments_TenantId_EmployeeCodeRuleId_SequenceOrd~",
                table: "EmployeeCodeSegments",
                columns: new[] { "TenantId", "EmployeeCodeRuleId", "SequenceOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeSequences_TenantId_EmployeeCodeRuleId_Scope_Scop~",
                table: "EmployeeCodeSequences",
                columns: new[] { "TenantId", "EmployeeCodeRuleId", "Scope", "ScopeKey", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeContacts_TenantId_EmployeeId",
                table: "EmployeeContacts",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_TenantId_EmployeeId",
                table: "EmployeeDocuments",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_TenantId_PreviousEmploymentId",
                table: "EmployeeDocuments",
                columns: new[] { "TenantId", "PreviousEmploymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEducationRecords_TenantId_EmployeeId",
                table: "EmployeeEducationRecords",
                columns: new[] { "TenantId", "EmployeeId" });

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
                name: "IX_EmployeeEmploymentHistory_TenantId_EmployeeId_EffectiveFrom",
                table: "EmployeeEmploymentHistory",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_TenantId_EmployeeId_EffectiveTo",
                table: "EmployeeEmploymentHistory",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentHistory_WorkLocationId",
                table: "EmployeeEmploymentHistory",
                column: "WorkLocationId");

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
                name: "IX_EmployeeFamilyMembers_TenantId_EmployeeId",
                table: "EmployeeFamilyMembers",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveBalances_TenantId_EmployeeId_LeaveTypeId_LeaveP~",
                table: "EmployeeLeaveBalances",
                columns: new[] { "TenantId", "EmployeeId", "LeaveTypeId", "LeavePeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveBalances_TenantId_LeavePeriodId",
                table: "EmployeeLeaveBalances",
                columns: new[] { "TenantId", "LeavePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveBalances_TenantId_LeaveTypeId",
                table: "EmployeeLeaveBalances",
                columns: new[] { "TenantId", "LeaveTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePreviousEmployments_TenantId_EmployeeId",
                table: "EmployeePreviousEmployments",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BirthCityId",
                table: "Employees",
                column: "BirthCityId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BirthCountryId",
                table: "Employees",
                column: "BirthCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BirthStateId",
                table: "Employees",
                column: "BirthStateId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CostCenterId",
                table: "Employees",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_EmployeeTypeId",
                table: "Employees",
                column: "EmployeeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_DepartmentId",
                table: "Employees",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_DesignationId",
                table: "Employees",
                columns: new[] { "TenantId", "DesignationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_Email",
                table: "Employees",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_EmployeeCode",
                table: "Employees",
                columns: new[] { "TenantId", "EmployeeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_ReportingManagerId",
                table: "Employees",
                columns: new[] { "TenantId", "ReportingManagerId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSupervisors_TenantId_EmployeeId",
                table: "EmployeeSupervisors",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true);

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
                name: "IX_ImportBatches_TenantId_CreatedDate",
                table: "ImportBatches",
                columns: new[] { "TenantId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_ActorEmployeeId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "ActorEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_ActorUserId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_EmployeeId_LeaveTypeId_Lea~",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "EmployeeId", "LeaveTypeId", "LeavePeriodId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_EmployeeLeaveBalanceId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "EmployeeLeaveBalanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_IdempotencyKey",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_LeavePeriodId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "LeavePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_LeavePolicyRuleId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "LeavePolicyRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_LeavePolicyVersionId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "LeavePolicyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_LeaveRequestId_Transaction~",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "LeaveRequestId", "TransactionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_LeaveTypeId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "LeaveTypeId" });

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
                name: "IX_LeavePolicyAttachmentRules_TenantId_LeavePolicyRuleId",
                table: "LeavePolicyAttachmentRules",
                columns: new[] { "TenantId", "LeavePolicyRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyCalendarRules_TenantId_LeavePolicyRuleId",
                table: "LeavePolicyCalendarRules",
                columns: new[] { "TenantId", "LeavePolicyRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyCancellationRules_TenantId_LeavePolicyRuleId",
                table: "LeavePolicyCancellationRules",
                columns: new[] { "TenantId", "LeavePolicyRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyClubbingRules_TenantId_LeavePolicyVersionId_Highe~",
                table: "LeavePolicyClubbingRules",
                columns: new[] { "TenantId", "LeavePolicyVersionId", "HigherLeavePolicyRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyClubbingRules_TenantId_LeavePolicyVersionId_Lower~",
                table: "LeavePolicyClubbingRules",
                columns: new[] { "TenantId", "LeavePolicyVersionId", "LowerLeavePolicyRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyClubbingRules_TenantId_LeavePolicyVersionId_Norma~",
                table: "LeavePolicyClubbingRules",
                columns: new[] { "TenantId", "LeavePolicyVersionId", "NormalizedPairKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyEligibilityRules_TenantId_LeavePolicyRuleId",
                table: "LeavePolicyEligibilityRules",
                columns: new[] { "TenantId", "LeavePolicyRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyEntitlementRules_TenantId_LeavePolicyRuleId",
                table: "LeavePolicyEntitlementRules",
                columns: new[] { "TenantId", "LeavePolicyRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyRequestRules_TenantId_LeavePolicyRuleId",
                table: "LeavePolicyRequestRules",
                columns: new[] { "TenantId", "LeavePolicyRuleId" },
                unique: true);

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
                name: "IX_LeaveRequestDays_TenantId_LeaveRequestId_Date",
                table: "LeaveRequestDays",
                columns: new[] { "TenantId", "LeaveRequestId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestEvents_TenantId_ActorEmployeeId",
                table: "LeaveRequestEvents",
                columns: new[] { "TenantId", "ActorEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestEvents_TenantId_ActorUserId",
                table: "LeaveRequestEvents",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestEvents_TenantId_LeaveRequestId_OccurredAtUtc_Id",
                table: "LeaveRequestEvents",
                columns: new[] { "TenantId", "LeaveRequestId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_EmployeeId_EmployeeEmploymentHistoryId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "EmployeeId", "EmployeeEmploymentHistoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_EmployeeId_IdempotencyKey",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "EmployeeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_EmployeeId_StartDate_EndDate_Status",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "EmployeeId", "StartDate", "EndDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_LeavePeriodId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "LeavePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_LeavePolicyVersionId_LeavePolicyRuleId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "LeavePolicyVersionId", "LeavePolicyRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_LeaveTypeId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "LeaveTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_TenantId_Code",
                table: "LeaveTypes",
                columns: new[] { "TenantId", "Code" },
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
                name: "IX_Permissions_Name",
                table: "Permissions",
                column: "Name",
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
                name: "IX_RefreshTokens_TenantId_UserId",
                table: "RefreshTokens",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
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
                name: "IX_States_CountryId_Code",
                table: "States",
                columns: new[] { "CountryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_States_CountryId_Name",
                table: "States",
                columns: new[] { "CountryId", "Name" },
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
                name: "IX_Tenants_Host",
                table: "Tenants",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ShardKey",
                table: "Tenants",
                column: "ShardKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantCode",
                table: "Tenants",
                column: "TenantCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_TenantId",
                table: "UserRoles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountEmployeeCurrentLinks");

            migrationBuilder.DropTable(
                name: "EmployeeAdditionalInfo");

            migrationBuilder.DropTable(
                name: "EmployeeAddresses");

            migrationBuilder.DropTable(
                name: "EmployeeAuditLogs");

            migrationBuilder.DropTable(
                name: "EmployeeBankDetails");

            migrationBuilder.DropTable(
                name: "EmployeeCodeRuleConditions");

            migrationBuilder.DropTable(
                name: "EmployeeCodeSegments");

            migrationBuilder.DropTable(
                name: "EmployeeCodeSequences");

            migrationBuilder.DropTable(
                name: "EmployeeContacts");

            migrationBuilder.DropTable(
                name: "EmployeeDocuments");

            migrationBuilder.DropTable(
                name: "EmployeeEducationRecords");

            migrationBuilder.DropTable(
                name: "EmployeeEmployments");

            migrationBuilder.DropTable(
                name: "EmployeeFamilyMembers");

            migrationBuilder.DropTable(
                name: "EmployeeSupervisors");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "LeaveBalanceTransactions");

            migrationBuilder.DropTable(
                name: "LeavePolicyApplicabilitySets");

            migrationBuilder.DropTable(
                name: "LeavePolicyAttachmentRules");

            migrationBuilder.DropTable(
                name: "LeavePolicyCalendarRules");

            migrationBuilder.DropTable(
                name: "LeavePolicyCancellationRules");

            migrationBuilder.DropTable(
                name: "LeavePolicyClubbingRules");

            migrationBuilder.DropTable(
                name: "LeavePolicyEligibilityRules");

            migrationBuilder.DropTable(
                name: "LeavePolicyEntitlementRules");

            migrationBuilder.DropTable(
                name: "LeavePolicyRequestRules");

            migrationBuilder.DropTable(
                name: "LeaveRequestDays");

            migrationBuilder.DropTable(
                name: "LeaveRequestEvents");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "AccountEmployeeLinkEvents");

            migrationBuilder.DropTable(
                name: "Banks");

            migrationBuilder.DropTable(
                name: "EmployeeCodeRules");

            migrationBuilder.DropTable(
                name: "EmployeePreviousEmployments");

            migrationBuilder.DropTable(
                name: "EmployeeLeaveBalances");

            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "EmployeeCodeConfigVersions");

            migrationBuilder.DropTable(
                name: "EmployeeEmploymentHistory");

            migrationBuilder.DropTable(
                name: "LeavePeriods");

            migrationBuilder.DropTable(
                name: "LeavePolicyRules");

            migrationBuilder.DropTable(
                name: "EmployeeCodeConfigs");

            migrationBuilder.DropTable(
                name: "Employees");

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
                name: "LeavePolicyVersions");

            migrationBuilder.DropTable(
                name: "LeaveTypes");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "CostCenters");

            migrationBuilder.DropTable(
                name: "Designations");

            migrationBuilder.DropTable(
                name: "EmployeeTypes");

            migrationBuilder.DropTable(
                name: "HoldingCompanies");

            migrationBuilder.DropTable(
                name: "Functions");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "LeavePolicies");

            migrationBuilder.DropTable(
                name: "States");

            migrationBuilder.DropTable(
                name: "SubDepartments");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
