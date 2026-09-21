using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddGratuitySeparationBenefitsMySqlPhase7P : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeparationReason",
                table: "FinalSettlementCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GratuityPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GratuityPolicies", x => x.Id);
                    table.UniqueConstraint("AK_GratuityPolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_GratuityPolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveEncashmentCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FinalSettlementId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EligibleDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EncashableDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    WageBasisAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Divisor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    NonTaxableAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SourceBalanceReference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    CalculationDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveEncashmentCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveEncashmentCalculations_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveEncashmentCalculations_FinalSettlementCases_TenantId_Fi~",
                        columns: x => new { x.TenantId, x.FinalSettlementId },
                        principalTable: "FinalSettlementCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaveEncashmentCalculations_LeaveTypes_TenantId_LeaveTypeId",
                        columns: x => new { x.TenantId, x.LeaveTypeId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveEncashmentCalculations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NoticeSettlementCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FinalSettlementId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    RequiredDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ServedDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DifferenceDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    WageBasisAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Divisor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    NonTaxableAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CalculationDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeSettlementCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeSettlementCalculations_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NoticeSettlementCalculations_FinalSettlementCases_TenantId_F~",
                        columns: x => new { x.TenantId, x.FinalSettlementId },
                        principalTable: "FinalSettlementCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoticeSettlementCalculations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GratuityPolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GratuityPolicyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MinimumServiceMonths = table.Column<int>(type: "int", nullable: false),
                    ServiceRoundingMethod = table.Column<int>(type: "int", nullable: false),
                    ServiceRoundingThresholdMonths = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    FormulaType = table.Column<int>(type: "int", nullable: false),
                    NumeratorDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    DenominatorDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    WageBasisType = table.Column<int>(type: "int", nullable: false),
                    SelectedSalaryComponentIdsJson = table.Column<string>(type: "longtext", nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    MaximumBenefit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    MinimumBenefit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    MonetaryRoundingMethod = table.Column<int>(type: "int", nullable: false),
                    RoundingPrecision = table.Column<int>(type: "int", nullable: false),
                    TaxTreatment = table.Column<int>(type: "int", nullable: false),
                    TaxablePercentage = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    IsResignationEligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsRetirementEligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsTerminationEligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDeathEligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDisabilityEligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsRedundancyEligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowMinimumServiceOverrideForDeath = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowMinimumServiceOverrideForDisability = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IncludeNoticePeriodInService = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LeaveEncashmentEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LeaveTypeIdsJson = table.Column<string>(type: "longtext", nullable: true),
                    MaximumLeaveEncashmentDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LeaveEncashmentDivisor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LeaveEncashmentTaxTreatment = table.Column<int>(type: "int", nullable: false),
                    NoticeSettlementType = table.Column<int>(type: "int", nullable: false),
                    NoticeDivisor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    NoticeWageBasisType = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GratuityPolicyVersions", x => x.Id);
                    table.UniqueConstraint("AK_GratuityPolicyVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_GratuityPolicyVersions_GratuityPolicies_TenantId_GratuityPol~",
                        columns: x => new { x.TenantId, x.GratuityPolicyId },
                        principalTable: "GratuityPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GratuityPolicyVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GratuityCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FinalSettlementId = table.Column<Guid>(type: "char(36)", nullable: true),
                    GratuityPolicyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GratuityPolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SeparationReason = table.Column<int>(type: "int", nullable: false),
                    ServiceStartDate = table.Column<DateTime>(type: "date", nullable: false),
                    ServiceEndDate = table.Column<DateTime>(type: "date", nullable: false),
                    TotalServiceDays = table.Column<int>(type: "int", nullable: false),
                    TotalServiceMonths = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EligibleServiceUnits = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AppliedRoundingRule = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    WageBasisType = table.Column<int>(type: "int", nullable: false),
                    WageBasisAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    WageSnapshotJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    NumeratorDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    DenominatorDays = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    GrossCalculatedAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CapApplied = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CapAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    FinalGratuityAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    NonTaxableAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    CalculationDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CalculatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GratuityCalculations", x => x.Id);
                    table.UniqueConstraint("AK_GratuityCalculations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_GratuityCalculations_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GratuityCalculations_FinalSettlementCases_TenantId_FinalSett~",
                        columns: x => new { x.TenantId, x.FinalSettlementId },
                        principalTable: "FinalSettlementCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GratuityCalculations_GratuityPolicies_TenantId_GratuityPolic~",
                        columns: x => new { x.TenantId, x.GratuityPolicyId },
                        principalTable: "GratuityPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GratuityCalculations_GratuityPolicyVersions_TenantId_Gratuit~",
                        columns: x => new { x.TenantId, x.GratuityPolicyVersionId },
                        principalTable: "GratuityPolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GratuityCalculations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SeparationBenefitHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GratuityPolicyId = table.Column<Guid>(type: "char(36)", nullable: true),
                    GratuityPolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    FinalSettlementId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OriginalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FinalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    SnapshotJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationBenefitHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationBenefitHistories_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationBenefitHistories_FinalSettlementCases_TenantId_Fin~",
                        columns: x => new { x.TenantId, x.FinalSettlementId },
                        principalTable: "FinalSettlementCases",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationBenefitHistories_GratuityPolicies_TenantId_Gratuit~",
                        columns: x => new { x.TenantId, x.GratuityPolicyId },
                        principalTable: "GratuityPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationBenefitHistories_GratuityPolicyVersions_TenantId_G~",
                        columns: x => new { x.TenantId, x.GratuityPolicyVersionId },
                        principalTable: "GratuityPolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationBenefitHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GratuityOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GratuityCalculationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OverrideAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GratuityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GratuityOverrides_GratuityCalculations_TenantId_GratuityCalc~",
                        columns: x => new { x.TenantId, x.GratuityCalculationId },
                        principalTable: "GratuityCalculations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GratuityOverrides_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GratuityCalculations_TenantId_EmployeeId_ServiceEndDate",
                table: "GratuityCalculations",
                columns: new[] { "TenantId", "EmployeeId", "ServiceEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GratuityCalculations_TenantId_FinalSettlementId",
                table: "GratuityCalculations",
                columns: new[] { "TenantId", "FinalSettlementId" },
                unique: true,
                filter: "[FinalSettlementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GratuityCalculations_TenantId_GratuityPolicyId",
                table: "GratuityCalculations",
                columns: new[] { "TenantId", "GratuityPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_GratuityCalculations_TenantId_GratuityPolicyVersionId",
                table: "GratuityCalculations",
                columns: new[] { "TenantId", "GratuityPolicyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_GratuityOverrides_TenantId_GratuityCalculationId_ApprovedAtU~",
                table: "GratuityOverrides",
                columns: new[] { "TenantId", "GratuityCalculationId", "ApprovedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GratuityPolicies_TenantId_Code",
                table: "GratuityPolicies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GratuityPolicyVersions_TenantId_GratuityPolicyId_EffectiveFr~",
                table: "GratuityPolicyVersions",
                columns: new[] { "TenantId", "GratuityPolicyId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEncashmentCalculations_TenantId_EmployeeId",
                table: "LeaveEncashmentCalculations",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEncashmentCalculations_TenantId_FinalSettlementId_Leave~",
                table: "LeaveEncashmentCalculations",
                columns: new[] { "TenantId", "FinalSettlementId", "LeaveTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEncashmentCalculations_TenantId_LeaveTypeId",
                table: "LeaveEncashmentCalculations",
                columns: new[] { "TenantId", "LeaveTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeSettlementCalculations_TenantId_EmployeeId",
                table: "NoticeSettlementCalculations",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeSettlementCalculations_TenantId_FinalSettlementId",
                table: "NoticeSettlementCalculations",
                columns: new[] { "TenantId", "FinalSettlementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationBenefitHistories_TenantId_EmployeeId_OccurredAtUtc",
                table: "SeparationBenefitHistories",
                columns: new[] { "TenantId", "EmployeeId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationBenefitHistories_TenantId_FinalSettlementId",
                table: "SeparationBenefitHistories",
                columns: new[] { "TenantId", "FinalSettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationBenefitHistories_TenantId_GratuityPolicyId",
                table: "SeparationBenefitHistories",
                columns: new[] { "TenantId", "GratuityPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationBenefitHistories_TenantId_GratuityPolicyVersionId",
                table: "SeparationBenefitHistories",
                columns: new[] { "TenantId", "GratuityPolicyVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GratuityOverrides");

            migrationBuilder.DropTable(
                name: "LeaveEncashmentCalculations");

            migrationBuilder.DropTable(
                name: "NoticeSettlementCalculations");

            migrationBuilder.DropTable(
                name: "SeparationBenefitHistories");

            migrationBuilder.DropTable(
                name: "GratuityCalculations");

            migrationBuilder.DropTable(
                name: "GratuityPolicyVersions");

            migrationBuilder.DropTable(
                name: "GratuityPolicies");

            migrationBuilder.DropColumn(
                name: "SeparationReason",
                table: "FinalSettlementCases");
        }
    }
}
