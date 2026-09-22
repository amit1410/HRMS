using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAdjustmentsOffCyclePhase7R : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollAdjustments_PayrollRetroResults_TenantId_SourceId",
                table: "PayrollAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustments_TenantId_SourceId",
                table: "PayrollAdjustments");

            migrationBuilder.AddColumn<string>(
                name: "AdjustmentNumber",
                table: "PayrollAdjustments",
                type: "varchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "AppliedAmount",
                table: "PayrollAdjustments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "PayrollAdjustments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "PayrollAdjustments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyVersion",
                table: "PayrollAdjustments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "PayrollAdjustments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveDate",
                table: "PayrollAdjustments",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalPayrollResultId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalPayrollRunId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollPeriodId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "PayrollAdjustments",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReasonCodeId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAtUtc",
                table: "PayrollAdjustments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetroResultId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalaryComponentId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SettlementMethod",
                table: "PayrollAdjustments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceReferenceId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatutoryTreatment",
                table: "PayrollAdjustments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "PayrollAdjustments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByUserId",
                table: "PayrollAdjustments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxTreatment",
                table: "PayrollAdjustments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PayrollAdjustments_TenantId_Id",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "PayrollAdjustmentApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollAdjustmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollResultId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AppliedDate = table.Column<DateTime>(type: "date", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustmentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentApplications_PayrollAdjustments_TenantId_Pa~",
                        columns: x => new { x.TenantId, x.PayrollAdjustmentId },
                        principalTable: "PayrollAdjustments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentApplications_PayrollResults_TenantId_Payrol~",
                        columns: x => new { x.TenantId, x.PayrollResultId },
                        principalTable: "PayrollResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentApplications_PayrollRuns_TenantId_PayrollRu~",
                        columns: x => new { x.TenantId, x.PayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentApplications_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollAdjustmentHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollAdjustmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentHistories_PayrollAdjustments_TenantId_Payro~",
                        columns: x => new { x.TenantId, x.PayrollAdjustmentId },
                        principalTable: "PayrollAdjustments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollAdjustmentNumberSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    NextValue = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustmentNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentNumberSequences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollAdjustmentReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequiresComment = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowedAdjustmentTypes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustmentReasons", x => x.Id);
                    table.UniqueConstraint("AK_PayrollAdjustmentReasons_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollAdjustmentReasons_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollCorrectionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollAdjustmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OriginalPayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OriginalPayrollResultId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OriginalGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeltaGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeltaDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeltaNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollCorrectionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollCorrectionSnapshots_PayrollAdjustments_TenantId_Payro~",
                        columns: x => new { x.TenantId, x.PayrollAdjustmentId },
                        principalTable: "PayrollAdjustments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollCorrectionSnapshots_PayrollResults_TenantId_OriginalP~",
                        columns: x => new { x.TenantId, x.OriginalPayrollResultId },
                        principalTable: "PayrollResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollCorrectionSnapshots_PayrollRuns_TenantId_OriginalPayr~",
                        columns: x => new { x.TenantId, x.OriginalPayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollCorrectionSnapshots_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OriginalPayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OriginalPayrollResultId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ReversalRunId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ReasonCodeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Reason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollReversals_PayrollResults_TenantId_OriginalPayrollResu~",
                        columns: x => new { x.TenantId, x.OriginalPayrollResultId },
                        principalTable: "PayrollResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollReversals_PayrollRuns_TenantId_OriginalPayrollRunId",
                        columns: x => new { x.TenantId, x.OriginalPayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollReversals_PayrollRuns_TenantId_ReversalRunId",
                        columns: x => new { x.TenantId, x.ReversalRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollReversals_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_RetroResultId",
                table: "PayrollAdjustments",
                column: "RetroResultId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_AdjustmentNumber",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "AdjustmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_PayrollPeriodId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "PayrollPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_ReasonCodeId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "ReasonCodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollAdjustmentId_P~",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "PayrollAdjustmentId", "PayrollRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollResultId",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "PayrollResultId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollRunId",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "PayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentHistories_TenantId_PayrollAdjustmentId_Occu~",
                table: "PayrollAdjustmentHistories",
                columns: new[] { "TenantId", "PayrollAdjustmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentNumberSequences_TenantId_Year",
                table: "PayrollAdjustmentNumberSequences",
                columns: new[] { "TenantId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentReasons_TenantId_Code",
                table: "PayrollAdjustmentReasons",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCorrectionSnapshots_TenantId_OriginalPayrollResultId",
                table: "PayrollCorrectionSnapshots",
                columns: new[] { "TenantId", "OriginalPayrollResultId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCorrectionSnapshots_TenantId_OriginalPayrollRunId",
                table: "PayrollCorrectionSnapshots",
                columns: new[] { "TenantId", "OriginalPayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCorrectionSnapshots_TenantId_PayrollAdjustmentId",
                table: "PayrollCorrectionSnapshots",
                columns: new[] { "TenantId", "PayrollAdjustmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReversals_TenantId_OriginalPayrollResultId",
                table: "PayrollReversals",
                columns: new[] { "TenantId", "OriginalPayrollResultId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReversals_TenantId_OriginalPayrollRunId",
                table: "PayrollReversals",
                columns: new[] { "TenantId", "OriginalPayrollRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReversals_TenantId_ReversalRunId",
                table: "PayrollReversals",
                columns: new[] { "TenantId", "ReversalRunId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollAdjustments_PayrollAdjustmentReasons_TenantId_ReasonC~",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "ReasonCodeId" },
                principalTable: "PayrollAdjustmentReasons",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollAdjustments_PayrollPeriods_TenantId_PayrollPeriodId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "PayrollPeriodId" },
                principalTable: "PayrollPeriods",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollAdjustments_PayrollRetroResults_RetroResultId",
                table: "PayrollAdjustments",
                column: "RetroResultId",
                principalTable: "PayrollRetroResults",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollAdjustments_PayrollAdjustmentReasons_TenantId_ReasonC~",
                table: "PayrollAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollAdjustments_PayrollPeriods_TenantId_PayrollPeriodId",
                table: "PayrollAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollAdjustments_PayrollRetroResults_RetroResultId",
                table: "PayrollAdjustments");

            migrationBuilder.DropTable(
                name: "PayrollAdjustmentApplications");

            migrationBuilder.DropTable(
                name: "PayrollAdjustmentHistories");

            migrationBuilder.DropTable(
                name: "PayrollAdjustmentNumberSequences");

            migrationBuilder.DropTable(
                name: "PayrollAdjustmentReasons");

            migrationBuilder.DropTable(
                name: "PayrollCorrectionSnapshots");

            migrationBuilder.DropTable(
                name: "PayrollReversals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PayrollAdjustments_TenantId_Id",
                table: "PayrollAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustments_RetroResultId",
                table: "PayrollAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustments_TenantId_AdjustmentNumber",
                table: "PayrollAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustments_TenantId_PayrollPeriodId",
                table: "PayrollAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustments_TenantId_ReasonCodeId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "AdjustmentNumber",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "AppliedAmount",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "OriginalPayrollResultId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "OriginalPayrollRunId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "PayrollPeriodId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "ReasonCodeId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "RetroResultId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "SalaryComponentId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "SettlementMethod",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "SourceReferenceId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "StatutoryTreatment",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "TaxTreatment",
                table: "PayrollAdjustments");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TenantId_SourceId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "SourceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollAdjustments_PayrollRetroResults_TenantId_SourceId",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "SourceId" },
                principalTable: "PayrollRetroResults",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
