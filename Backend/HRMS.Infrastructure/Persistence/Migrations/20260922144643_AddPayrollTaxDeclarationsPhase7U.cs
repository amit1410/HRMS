using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollTaxDeclarationsPhase7U : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxDeclarationCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CategoryType = table.Column<int>(type: "int", nullable: false),
                    RequiresProof = table.Column<bool>(type: "bit", nullable: false),
                    AllowsMultipleEntries = table.Column<bool>(type: "bit", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxDeclarationCategories", x => x.Id);
                    table.UniqueConstraint("AK_TaxDeclarationCategories_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_TaxDeclarationCategories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxDeclarationCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FinancialYear = table.Column<int>(type: "int", nullable: false),
                    DeclarationOpenDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeclarationCloseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProofSubmissionOpenDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProofSubmissionCloseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxDeclarationCycles", x => x.Id);
                    table.UniqueConstraint("AK_TaxDeclarationCycles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_TaxDeclarationCycles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxDeclarationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxDeclarationCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiresProof = table.Column<bool>(type: "bit", nullable: false),
                    AllowsAmount = table.Column<bool>(type: "bit", nullable: false),
                    AllowsReferenceNumber = table.Column<bool>(type: "bit", nullable: false),
                    AllowsDate = table.Column<bool>(type: "bit", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    PayrollTaxInputCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StatutoryMappingCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxDeclarationItems", x => x.Id);
                    table.UniqueConstraint("AK_TaxDeclarationItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_TaxDeclarationItems_TaxDeclarationCategories_TenantId_TaxDeclarationCategoryId",
                        columns: x => new { x.TenantId, x.TaxDeclarationCategoryId },
                        principalTable: "TaxDeclarationCategories",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxDeclarationItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeTaxDeclarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxDeclarationCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTaxDeclarations", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeTaxDeclarations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeTaxDeclarations_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeTaxDeclarations_TaxDeclarationCycles_TenantId_TaxDeclarationCycleId",
                        columns: x => new { x.TenantId, x.TaxDeclarationCycleId },
                        principalTable: "TaxDeclarationCycles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeTaxDeclarations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeTaxDeclarationLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeTaxDeclarationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxDeclarationCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxDeclarationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeclaredAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DeclarationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewerComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTaxDeclarationLines", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeTaxDeclarationLines_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeTaxDeclarationLines_EmployeeTaxDeclarations_TenantId_EmployeeTaxDeclarationId",
                        columns: x => new { x.TenantId, x.EmployeeTaxDeclarationId },
                        principalTable: "EmployeeTaxDeclarations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeTaxDeclarationLines_TaxDeclarationCategories_TenantId_TaxDeclarationCategoryId",
                        columns: x => new { x.TenantId, x.TaxDeclarationCategoryId },
                        principalTable: "TaxDeclarationCategories",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeTaxDeclarationLines_TaxDeclarationItems_TenantId_TaxDeclarationItemId",
                        columns: x => new { x.TenantId, x.TaxDeclarationItemId },
                        principalTable: "TaxDeclarationItems",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeTaxDeclarationLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxDeclarationAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeTaxDeclarationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeTaxDeclarationLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxDeclarationAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxDeclarationAuditEvents_EmployeeTaxDeclarations_TenantId_EmployeeTaxDeclarationId",
                        columns: x => new { x.TenantId, x.EmployeeTaxDeclarationId },
                        principalTable: "EmployeeTaxDeclarations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxDeclarationAuditEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxDeclarationProofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeTaxDeclarationLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    StorageReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewerComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Hash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxDeclarationProofs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxDeclarationProofs_EmployeeTaxDeclarationLines_TenantId_EmployeeTaxDeclarationLineId",
                        columns: x => new { x.TenantId, x.EmployeeTaxDeclarationLineId },
                        principalTable: "EmployeeTaxDeclarationLines",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxDeclarationProofs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxDeclarationLines_TenantId_EmployeeTaxDeclarationId",
                table: "EmployeeTaxDeclarationLines",
                columns: new[] { "TenantId", "EmployeeTaxDeclarationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxDeclarationLines_TenantId_TaxDeclarationCategoryId",
                table: "EmployeeTaxDeclarationLines",
                columns: new[] { "TenantId", "TaxDeclarationCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxDeclarationLines_TenantId_TaxDeclarationItemId",
                table: "EmployeeTaxDeclarationLines",
                columns: new[] { "TenantId", "TaxDeclarationItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxDeclarations_TenantId_EmployeeId_TaxDeclarationCycleId",
                table: "EmployeeTaxDeclarations",
                columns: new[] { "TenantId", "EmployeeId", "TaxDeclarationCycleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxDeclarations_TenantId_Status",
                table: "EmployeeTaxDeclarations",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxDeclarations_TenantId_TaxDeclarationCycleId",
                table: "EmployeeTaxDeclarations",
                columns: new[] { "TenantId", "TaxDeclarationCycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeclarationAuditEvents_TenantId_EmployeeTaxDeclarationId_OccurredAtUtc",
                table: "TaxDeclarationAuditEvents",
                columns: new[] { "TenantId", "EmployeeTaxDeclarationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeclarationCategories_TenantId_Code",
                table: "TaxDeclarationCategories",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeclarationCycles_TenantId_Code",
                table: "TaxDeclarationCycles",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeclarationCycles_TenantId_FinancialYear_Status",
                table: "TaxDeclarationCycles",
                columns: new[] { "TenantId", "FinancialYear", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeclarationItems_TenantId_TaxDeclarationCategoryId_Code",
                table: "TaxDeclarationItems",
                columns: new[] { "TenantId", "TaxDeclarationCategoryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeclarationProofs_TenantId_EmployeeTaxDeclarationLineId",
                table: "TaxDeclarationProofs",
                columns: new[] { "TenantId", "EmployeeTaxDeclarationLineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxDeclarationAuditEvents");

            migrationBuilder.DropTable(
                name: "TaxDeclarationProofs");

            migrationBuilder.DropTable(
                name: "EmployeeTaxDeclarationLines");

            migrationBuilder.DropTable(
                name: "EmployeeTaxDeclarations");

            migrationBuilder.DropTable(
                name: "TaxDeclarationItems");

            migrationBuilder.DropTable(
                name: "TaxDeclarationCycles");

            migrationBuilder.DropTable(
                name: "TaxDeclarationCategories");
        }
    }
}
