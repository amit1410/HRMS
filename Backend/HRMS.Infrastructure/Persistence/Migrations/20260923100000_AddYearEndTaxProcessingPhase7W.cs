using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndTaxProcessingPhase7W : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YearEndTaxRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxYear = table.Column<int>(type: "int", nullable: false),
                    TaxYearCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    BlockingIssueCount = table.Column<int>(type: "int", nullable: false),
                    TotalTaxDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalExcessTax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndTaxRuns", x => x.Id);
                    table.UniqueConstraint("AK_YearEndTaxRuns_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_YearEndTaxRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "YearEndTaxEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YtdGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    YtdTaxableIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    YtdTaxDeducted = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedDeclarationAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedProofAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousEmployerTaxableIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousEmployerTaxDeducted = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProjectedRemainingTaxableIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProjectedAnnualTaxableIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProjectedAnnualTax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EstimatedTaxDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EstimatedExcessTax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalTaxableIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalTaxLiability = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BlockingIssueCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BlockingIssueMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CalculationSnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndTaxEmployees", x => x.Id);
                    table.UniqueConstraint("AK_YearEndTaxEmployees_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_YearEndTaxEmployees_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxEmployees_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxEmployees_YearEndTaxRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "YearEndTaxRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YearEndTaxHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Event = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndTaxHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndTaxHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxHistories_YearEndTaxRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "YearEndTaxRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YearEndTaxPreviousEmployerInputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployerReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TaxableIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxDeducted = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EligibleDeductionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndTaxPreviousEmployerInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndTaxPreviousEmployerInputs_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxPreviousEmployerInputs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxPreviousEmployerInputs_YearEndTaxRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "YearEndTaxRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YearEndTaxAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YearEndTaxEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PayrollAdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SourceCalculationReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndTaxAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndTaxAdjustments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxAdjustments_PayrollAdjustments_TenantId_PayrollAdjustmentId",
                        columns: x => new { x.TenantId, x.PayrollAdjustmentId },
                        principalTable: "PayrollAdjustments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxAdjustments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxAdjustments_YearEndTaxEmployees_TenantId_YearEndTaxEmployeeId",
                        columns: x => new { x.TenantId, x.YearEndTaxEmployeeId },
                        principalTable: "YearEndTaxEmployees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxAdjustments_YearEndTaxRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "YearEndTaxRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YearEndTaxStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YearEndTaxEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatementReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 12000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndTaxStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndTaxStatements_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxStatements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxStatements_YearEndTaxEmployees_TenantId_YearEndTaxEmployeeId",
                        columns: x => new { x.TenantId, x.YearEndTaxEmployeeId },
                        principalTable: "YearEndTaxEmployees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndTaxStatements_YearEndTaxRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "YearEndTaxRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxAdjustments_TenantId_EmployeeId",
                table: "YearEndTaxAdjustments",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxAdjustments_TenantId_PayrollAdjustmentId",
                table: "YearEndTaxAdjustments",
                columns: new[] { "TenantId", "PayrollAdjustmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxAdjustments_TenantId_RunId_EmployeeId",
                table: "YearEndTaxAdjustments",
                columns: new[] { "TenantId", "RunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxAdjustments_TenantId_YearEndTaxEmployeeId",
                table: "YearEndTaxAdjustments",
                columns: new[] { "TenantId", "YearEndTaxEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxEmployees_TenantId_EmployeeId",
                table: "YearEndTaxEmployees",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxEmployees_TenantId_RunId_EmployeeId",
                table: "YearEndTaxEmployees",
                columns: new[] { "TenantId", "RunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxEmployees_TenantId_RunId_Status",
                table: "YearEndTaxEmployees",
                columns: new[] { "TenantId", "RunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxHistories_TenantId_RunId_OccurredAtUtc",
                table: "YearEndTaxHistories",
                columns: new[] { "TenantId", "RunId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxPreviousEmployerInputs_TenantId_EmployeeId",
                table: "YearEndTaxPreviousEmployerInputs",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxPreviousEmployerInputs_TenantId_RunId_EmployeeId_EmployerReference",
                table: "YearEndTaxPreviousEmployerInputs",
                columns: new[] { "TenantId", "RunId", "EmployeeId", "EmployerReference" },
                unique: true,
                filter: "[EmployerReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxRuns_TenantId_Status",
                table: "YearEndTaxRuns",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxRuns_TenantId_TaxYear",
                table: "YearEndTaxRuns",
                columns: new[] { "TenantId", "TaxYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxStatements_TenantId_EmployeeId",
                table: "YearEndTaxStatements",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxStatements_TenantId_RunId_EmployeeId",
                table: "YearEndTaxStatements",
                columns: new[] { "TenantId", "RunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndTaxStatements_TenantId_YearEndTaxEmployeeId",
                table: "YearEndTaxStatements",
                columns: new[] { "TenantId", "YearEndTaxEmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YearEndTaxAdjustments");

            migrationBuilder.DropTable(
                name: "YearEndTaxHistories");

            migrationBuilder.DropTable(
                name: "YearEndTaxPreviousEmployerInputs");

            migrationBuilder.DropTable(
                name: "YearEndTaxStatements");

            migrationBuilder.DropTable(
                name: "YearEndTaxEmployees");

            migrationBuilder.DropTable(
                name: "YearEndTaxRuns");
        }
    }
}
