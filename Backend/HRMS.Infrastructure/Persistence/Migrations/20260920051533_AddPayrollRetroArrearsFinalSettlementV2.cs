using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRetroArrearsFinalSettlementV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinalSettlementCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeparationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastWorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    GrossPayable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetSettlement = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmployeeNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DepartmentSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DesignationSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LocationSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DateOfJoiningSnapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalSettlementCases", x => x.Id);
                    table.UniqueConstraint("AK_FinalSettlementCases_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_FinalSettlementCases_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinalSettlementCases_PayrollRuns_TenantId_PayrollRunId",
                        columns: x => new { x.TenantId, x.PayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinalSettlementCases_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRetroCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    TriggerSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DetectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRetroCases", x => x.Id);
                    table.UniqueConstraint("AK_PayrollRetroCases_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollRetroCases_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRetroCases_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinalSettlementHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalSettlementCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalSettlementHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalSettlementHistories_FinalSettlementCases_TenantId_FinalSettlementCaseId",
                        columns: x => new { x.TenantId, x.FinalSettlementCaseId },
                        principalTable: "FinalSettlementCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinalSettlementHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinalSettlementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalSettlementCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineType = table.Column<int>(type: "int", nullable: false),
                    ComponentCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsEarning = table.Column<bool>(type: "bit", nullable: false),
                    IsDeduction = table.Column<bool>(type: "bit", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalSettlementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalSettlementLines_FinalSettlementCases_TenantId_FinalSettlementCaseId",
                        columns: x => new { x.TenantId, x.FinalSettlementCaseId },
                        principalTable: "FinalSettlementCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinalSettlementLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRetroHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollRetroCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRetroHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRetroHistories_PayrollRetroCases_TenantId_PayrollRetroCaseId",
                        columns: x => new { x.TenantId, x.PayrollRetroCaseId },
                        principalTable: "PayrollRetroCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollRetroHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRetroResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollRetroCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalPayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalPayrollResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OriginalGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossDifference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalDeductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedDeductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeductionDifference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetDifference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalculationVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRetroResults", x => x.Id);
                    table.UniqueConstraint("AK_PayrollRetroResults_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollRetroResults_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRetroResults_PayrollResults_TenantId_OriginalPayrollResultId",
                        columns: x => new { x.TenantId, x.OriginalPayrollResultId },
                        principalTable: "PayrollResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRetroResults_PayrollRetroCases_TenantId_PayrollRetroCaseId",
                        columns: x => new { x.TenantId, x.PayrollRetroCaseId },
                        principalTable: "PayrollRetroCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollRetroResults_PayrollRuns_TenantId_OriginalPayrollRunId",
                        columns: x => new { x.TenantId, x.OriginalPayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRetroResults_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentType = table.Column<int>(type: "int", nullable: false),
                    ComponentCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TargetPayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_PayrollRetroResults_TenantId_SourceId",
                        columns: x => new { x.TenantId, x.SourceId },
                        principalTable: "PayrollRetroResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_PayrollRuns_TenantId_TargetPayrollRunId",
                        columns: x => new { x.TenantId, x.TargetPayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRetroComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollRetroResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalaryComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StatutoryType = table.Column<int>(type: "int", nullable: true),
                    ComponentCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComponentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DifferenceAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsEarning = table.Column<bool>(type: "bit", nullable: false),
                    IsDeduction = table.Column<bool>(type: "bit", nullable: false),
                    IsStatutory = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRetroComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRetroComponents_PayrollRetroResults_TenantId_PayrollRetroResultId",
                        columns: x => new { x.TenantId, x.PayrollRetroResultId },
                        principalTable: "PayrollRetroResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollRetroComponents_SalaryComponents_TenantId_SalaryComponentId",
                        columns: x => new { x.TenantId, x.SalaryComponentId },
                        principalTable: "SalaryComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRetroComponents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlementCases_TenantId_EmployeeId_SeparationDate",
                table: "FinalSettlementCases",
                columns: new[] { "TenantId", "EmployeeId", "SeparationDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlementCases_TenantId_PayrollRunId",
                table: "FinalSettlementCases",
                columns: new[] { "TenantId", "PayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlementCases_TenantId_Status",
                table: "FinalSettlementCases",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlementHistories_TenantId_FinalSettlementCaseId_ChangedAtUtc",
                table: "FinalSettlementHistories",
                columns: new[] { "TenantId", "FinalSettlementCaseId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlementLines_TenantId_FinalSettlementCaseId_Sequence",
                table: "FinalSettlementLines",
                columns: new[] { "TenantId", "FinalSettlementCaseId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_EmployeeId_Status",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_SourceId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_SourceType_SourceId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_TargetPayrollRunId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "TargetPayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroCases_TenantId_EffectiveFrom",
                table: "PayrollRetroCases",
                columns: new[] { "TenantId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroCases_TenantId_EmployeeId_Status",
                table: "PayrollRetroCases",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroComponents_TenantId_PayrollRetroResultId",
                table: "PayrollRetroComponents",
                columns: new[] { "TenantId", "PayrollRetroResultId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroComponents_TenantId_SalaryComponentId",
                table: "PayrollRetroComponents",
                columns: new[] { "TenantId", "SalaryComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroHistories_TenantId_PayrollRetroCaseId_ChangedAtUtc",
                table: "PayrollRetroHistories",
                columns: new[] { "TenantId", "PayrollRetroCaseId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroResults_TenantId_EmployeeId",
                table: "PayrollRetroResults",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroResults_TenantId_OriginalPayrollResultId",
                table: "PayrollRetroResults",
                columns: new[] { "TenantId", "OriginalPayrollResultId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroResults_TenantId_OriginalPayrollRunId",
                table: "PayrollRetroResults",
                columns: new[] { "TenantId", "OriginalPayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRetroResults_TenantId_PayrollRetroCaseId",
                table: "PayrollRetroResults",
                columns: new[] { "TenantId", "PayrollRetroCaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinalSettlementHistories");

            migrationBuilder.DropTable(
                name: "FinalSettlementLines");

            migrationBuilder.DropTable(
                name: "PayrollAdjustments");

            migrationBuilder.DropTable(
                name: "PayrollRetroComponents");

            migrationBuilder.DropTable(
                name: "PayrollRetroHistories");

            migrationBuilder.DropTable(
                name: "FinalSettlementCases");

            migrationBuilder.DropTable(
                name: "PayrollRetroResults");

            migrationBuilder.DropTable(
                name: "PayrollRetroCases");
        }
    }
}
