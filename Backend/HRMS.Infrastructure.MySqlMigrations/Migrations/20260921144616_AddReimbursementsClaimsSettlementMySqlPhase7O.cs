using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddReimbursementsClaimsSettlementMySqlPhase7O : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReimbursementClaimId",
                table: "PayrollResultComponents",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReimbursementClaimLineId",
                table: "PayrollResultComponents",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReimbursementCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CategoryType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    RequiresReceipt = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowsMultipleLines = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TaxTreatment = table.Column<int>(type: "int", nullable: false),
                    DefaultSettlementMethod = table.Column<int>(type: "int", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SettlementPriority = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementCategories", x => x.Id);
                    table.UniqueConstraint("AK_ReimbursementCategories_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReimbursementCategories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReimbursementClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClaimNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    ClaimDate = table.Column<DateTime>(type: "date", nullable: false),
                    ExpenseFromDate = table.Column<DateTime>(type: "date", nullable: true),
                    ExpenseToDate = table.Column<DateTime>(type: "date", nullable: true),
                    CurrencyCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    TotalClaimedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalEligibleAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonTaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SettlementMethod = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    RejectionReason = table.Column<string>(type: "longtext", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CancellationReason = table.Column<string>(type: "longtext", nullable: true),
                    SettledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SettledByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SettlementReference = table.Column<string>(type: "longtext", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementClaims", x => x.Id);
                    table.UniqueConstraint("AK_ReimbursementClaims_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReimbursementClaims_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReimbursementClaims_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReimbursementPolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementCategoryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    MinClaimAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxClaimAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PerTransactionLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MonthlyLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    YearlyLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RequiresReceipt = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReceiptRequiredAbove = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TaxTreatment = table.Column<int>(type: "int", nullable: false),
                    SettlementMethod = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EligibilityJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementPolicyVersions", x => x.Id);
                    table.UniqueConstraint("AK_ReimbursementPolicyVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReimbursementPolicyVersions_ReimbursementCategories_TenantId~",
                        columns: x => new { x.TenantId, x.ReimbursementCategoryId },
                        principalTable: "ReimbursementCategories",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReimbursementPolicyVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReimbursementHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementClaimId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: true),
                    ClaimedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    SourceType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReimbursementHistories_ReimbursementClaims_TenantId_Reimburs~",
                        columns: x => new { x.TenantId, x.ReimbursementClaimId },
                        principalTable: "ReimbursementClaims",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReimbursementHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReimbursementClaimLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementClaimId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementCategoryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ExpenseDate = table.Column<DateTime>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    ClaimedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EligibleAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonTaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MerchantName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ReceiptRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReceiptStatus = table.Column<int>(type: "int", nullable: false),
                    ApprovalComment = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementClaimLines", x => x.Id);
                    table.UniqueConstraint("AK_ReimbursementClaimLines_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReimbursementClaimLines_ReimbursementCategories_TenantId_Rei~",
                        columns: x => new { x.TenantId, x.ReimbursementCategoryId },
                        principalTable: "ReimbursementCategories",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReimbursementClaimLines_ReimbursementClaims_TenantId_Reimbur~",
                        columns: x => new { x.TenantId, x.ReimbursementClaimId },
                        principalTable: "ReimbursementClaims",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReimbursementClaimLines_ReimbursementPolicyVersions_TenantId~",
                        columns: x => new { x.TenantId, x.PolicyVersionId },
                        principalTable: "ReimbursementPolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReimbursementClaimLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReimbursementAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementClaimId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementClaimLineId = table.Column<Guid>(type: "char(36)", nullable: true),
                    FileName = table.Column<string>(type: "varchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    StorageReference = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReimbursementAttachments_ReimbursementClaimLines_TenantId_Re~",
                        columns: x => new { x.TenantId, x.ReimbursementClaimLineId },
                        principalTable: "ReimbursementClaimLines",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReimbursementAttachments_ReimbursementClaims_TenantId_Reimbu~",
                        columns: x => new { x.TenantId, x.ReimbursementClaimId },
                        principalTable: "ReimbursementClaims",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReimbursementAttachments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReimbursementSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementClaimId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReimbursementClaimLineId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SettlementType = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonTaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SettlementDate = table.Column<DateTime>(type: "date", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PayrollResultId = table.Column<Guid>(type: "char(36)", nullable: true),
                    FinalSettlementId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Reference = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReimbursementSettlements_ReimbursementClaimLines_TenantId_Re~",
                        columns: x => new { x.TenantId, x.ReimbursementClaimLineId },
                        principalTable: "ReimbursementClaimLines",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReimbursementSettlements_ReimbursementClaims_TenantId_Reimbu~",
                        columns: x => new { x.TenantId, x.ReimbursementClaimId },
                        principalTable: "ReimbursementClaims",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReimbursementSettlements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementAttachments_TenantId_ReimbursementClaimId",
                table: "ReimbursementAttachments",
                columns: new[] { "TenantId", "ReimbursementClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementAttachments_TenantId_ReimbursementClaimLineId",
                table: "ReimbursementAttachments",
                columns: new[] { "TenantId", "ReimbursementClaimLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementCategories_TenantId_Code",
                table: "ReimbursementCategories",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementClaimLines_TenantId_PolicyVersionId",
                table: "ReimbursementClaimLines",
                columns: new[] { "TenantId", "PolicyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementClaimLines_TenantId_ReimbursementCategoryId",
                table: "ReimbursementClaimLines",
                columns: new[] { "TenantId", "ReimbursementCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementClaimLines_TenantId_ReimbursementClaimId",
                table: "ReimbursementClaimLines",
                columns: new[] { "TenantId", "ReimbursementClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementClaims_TenantId_ClaimNumber",
                table: "ReimbursementClaims",
                columns: new[] { "TenantId", "ClaimNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementClaims_TenantId_EmployeeId_Status",
                table: "ReimbursementClaims",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementHistories_TenantId_ReimbursementClaimId_Occurre~",
                table: "ReimbursementHistories",
                columns: new[] { "TenantId", "ReimbursementClaimId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementPolicyVersions_TenantId_ReimbursementCategoryId~",
                table: "ReimbursementPolicyVersions",
                columns: new[] { "TenantId", "ReimbursementCategoryId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementSettlements_TenantId_FinalSettlementId",
                table: "ReimbursementSettlements",
                columns: new[] { "TenantId", "FinalSettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementSettlements_TenantId_PayrollResultId",
                table: "ReimbursementSettlements",
                columns: new[] { "TenantId", "PayrollResultId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementSettlements_TenantId_ReimbursementClaimId_Settl~",
                table: "ReimbursementSettlements",
                columns: new[] { "TenantId", "ReimbursementClaimId", "SettlementType" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementSettlements_TenantId_ReimbursementClaimLineId",
                table: "ReimbursementSettlements",
                columns: new[] { "TenantId", "ReimbursementClaimLineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReimbursementAttachments");

            migrationBuilder.DropTable(
                name: "ReimbursementHistories");

            migrationBuilder.DropTable(
                name: "ReimbursementSettlements");

            migrationBuilder.DropTable(
                name: "ReimbursementClaimLines");

            migrationBuilder.DropTable(
                name: "ReimbursementClaims");

            migrationBuilder.DropTable(
                name: "ReimbursementPolicyVersions");

            migrationBuilder.DropTable(
                name: "ReimbursementCategories");

            migrationBuilder.DropColumn(
                name: "ReimbursementClaimId",
                table: "PayrollResultComponents");

            migrationBuilder.DropColumn(
                name: "ReimbursementClaimLineId",
                table: "PayrollResultComponents");
        }
    }
}
