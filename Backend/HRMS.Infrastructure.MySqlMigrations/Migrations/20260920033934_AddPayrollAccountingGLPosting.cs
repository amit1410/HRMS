using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAccountingGLPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollAccountingConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAccountingConfigurations", x => x.Id);
                    table.UniqueConstraint("AK_PayrollAccountingConfigurations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollAccountingConfigurations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollGLAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    ExternalCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollGLAccounts", x => x.Id);
                    table.UniqueConstraint("AK_PayrollGLAccounts_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollGLAccounts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollAccountingConfigurationVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollAccountingConfigurationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AggregationMode = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAccountingConfigurationVersions", x => x.Id);
                    table.UniqueConstraint("AK_PayrollAccountingConfigurationVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollAccountingConfigurationVersions_PayrollAccountingConf~",
                        columns: x => new { x.TenantId, x.PayrollAccountingConfigurationId },
                        principalTable: "PayrollAccountingConfigurations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAccountingConfigurationVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollGLMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfigurationVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryComponentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    StatutoryType = table.Column<int>(type: "int", nullable: true),
                    MappingType = table.Column<int>(type: "int", nullable: false),
                    DebitAccountId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreditAccountId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployerContributionAccountId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollGLMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollGLMappings_PayrollAccountingConfigurationVersions_Ten~",
                        columns: x => new { x.TenantId, x.ConfigurationVersionId },
                        principalTable: "PayrollAccountingConfigurationVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollGLMappings_PayrollGLAccounts_TenantId_CreditAccountId",
                        columns: x => new { x.TenantId, x.CreditAccountId },
                        principalTable: "PayrollGLAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollGLMappings_PayrollGLAccounts_TenantId_DebitAccountId",
                        columns: x => new { x.TenantId, x.DebitAccountId },
                        principalTable: "PayrollGLAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollGLMappings_PayrollGLAccounts_TenantId_EmployerContrib~",
                        columns: x => new { x.TenantId, x.EmployerContributionAccountId },
                        principalTable: "PayrollGLAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollGLMappings_SalaryComponents_TenantId_SalaryComponentId",
                        columns: x => new { x.TenantId, x.SalaryComponentId },
                        principalTable: "SalaryComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollGLMappings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollJournalBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    JournalNumber = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    JournalDate = table.Column<DateTime>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    AccountingConfigurationVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GeneratedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ExportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExportedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollJournalBatches", x => x.Id);
                    table.UniqueConstraint("AK_PayrollJournalBatches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollJournalBatches_PayrollAccountingConfigurationVersions~",
                        columns: x => new { x.TenantId, x.AccountingConfigurationVersionId },
                        principalTable: "PayrollAccountingConfigurationVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollJournalBatches_PayrollPeriods_TenantId_PayrollPeriodId",
                        columns: x => new { x.TenantId, x.PayrollPeriodId },
                        principalTable: "PayrollPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollJournalBatches_PayrollRuns_TenantId_PayrollRunId",
                        columns: x => new { x.TenantId, x.PayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollJournalBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollJournalHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollJournalBatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollJournalHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollJournalHistories_PayrollJournalBatches_TenantId_Payro~",
                        columns: x => new { x.TenantId, x.PayrollJournalBatchId },
                        principalTable: "PayrollJournalBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollJournalHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollJournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollJournalBatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PayrollGLAccountId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AccountCodeSnapshot = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    AccountNameSnapshot = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    SourceType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SalaryComponentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    StatutoryType = table.Column<int>(type: "int", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollJournalLines", x => x.Id);
                    table.UniqueConstraint("AK_PayrollJournalLines_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollJournalLines_PayrollGLAccounts_TenantId_PayrollGLAcco~",
                        columns: x => new { x.TenantId, x.PayrollGLAccountId },
                        principalTable: "PayrollGLAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollJournalLines_PayrollJournalBatches_TenantId_PayrollJo~",
                        columns: x => new { x.TenantId, x.PayrollJournalBatchId },
                        principalTable: "PayrollJournalBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollJournalLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollJournalLineSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollJournalLineId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SourceType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollJournalLineSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollJournalLineSources_PayrollJournalLines_TenantId_Payro~",
                        columns: x => new { x.TenantId, x.PayrollJournalLineId },
                        principalTable: "PayrollJournalLines",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollJournalLineSources_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAccountingConfigurations_TenantId_Code",
                table: "PayrollAccountingConfigurations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAccountingConfigurationVersions_TenantId_PayrollAccou~",
                table: "PayrollAccountingConfigurationVersions",
                columns: new[] { "TenantId", "PayrollAccountingConfigurationId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollGLAccounts_TenantId_Code",
                table: "PayrollGLAccounts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollGLMappings_TenantId_ConfigurationVersionId_SalaryComp~",
                table: "PayrollGLMappings",
                columns: new[] { "TenantId", "ConfigurationVersionId", "SalaryComponentId", "StatutoryType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollGLMappings_TenantId_CreditAccountId",
                table: "PayrollGLMappings",
                columns: new[] { "TenantId", "CreditAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollGLMappings_TenantId_DebitAccountId",
                table: "PayrollGLMappings",
                columns: new[] { "TenantId", "DebitAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollGLMappings_TenantId_EmployerContributionAccountId",
                table: "PayrollGLMappings",
                columns: new[] { "TenantId", "EmployerContributionAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollGLMappings_TenantId_SalaryComponentId",
                table: "PayrollGLMappings",
                columns: new[] { "TenantId", "SalaryComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalBatches_TenantId_AccountingConfigurationVersio~",
                table: "PayrollJournalBatches",
                columns: new[] { "TenantId", "AccountingConfigurationVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalBatches_TenantId_JournalNumber",
                table: "PayrollJournalBatches",
                columns: new[] { "TenantId", "JournalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalBatches_TenantId_PayrollPeriodId",
                table: "PayrollJournalBatches",
                columns: new[] { "TenantId", "PayrollPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalBatches_TenantId_PayrollRunId",
                table: "PayrollJournalBatches",
                columns: new[] { "TenantId", "PayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalBatches_TenantId_Status",
                table: "PayrollJournalBatches",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalHistories_TenantId_PayrollJournalBatchId_Chang~",
                table: "PayrollJournalHistories",
                columns: new[] { "TenantId", "PayrollJournalBatchId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalLines_PayrollGLAccountId",
                table: "PayrollJournalLines",
                column: "PayrollGLAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalLines_TenantId_PayrollGLAccountId",
                table: "PayrollJournalLines",
                columns: new[] { "TenantId", "PayrollGLAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalLines_TenantId_PayrollJournalBatchId",
                table: "PayrollJournalLines",
                columns: new[] { "TenantId", "PayrollJournalBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalLineSources_TenantId_PayrollJournalLineId",
                table: "PayrollJournalLineSources",
                columns: new[] { "TenantId", "PayrollJournalLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalLineSources_TenantId_SourceType_SourceId",
                table: "PayrollJournalLineSources",
                columns: new[] { "TenantId", "SourceType", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollGLMappings");

            migrationBuilder.DropTable(
                name: "PayrollJournalHistories");

            migrationBuilder.DropTable(
                name: "PayrollJournalLineSources");

            migrationBuilder.DropTable(
                name: "PayrollJournalLines");

            migrationBuilder.DropTable(
                name: "PayrollGLAccounts");

            migrationBuilder.DropTable(
                name: "PayrollJournalBatches");

            migrationBuilder.DropTable(
                name: "PayrollAccountingConfigurationVersions");

            migrationBuilder.DropTable(
                name: "PayrollAccountingConfigurations");
        }
    }
}
