using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollStatutoryComplianceReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollCompliancePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ComplianceType = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "date", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollCompliancePeriods", x => x.Id);
                    table.UniqueConstraint("AK_PayrollCompliancePeriods_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollCompliancePeriods_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollStatutoryReturnBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollCompliancePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ComplianceType = table.Column<int>(type: "int", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    BatchNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    GrossRelevantWages = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeContribution = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployerContribution = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPayable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GeneratedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ValidatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ExportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExportedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    FiledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FiledByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ExternalReference = table.Column<string>(type: "longtext", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollStatutoryReturnBatches", x => x.Id);
                    table.UniqueConstraint("AK_PayrollStatutoryReturnBatches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnBatches_PayrollCompliancePeriods_Tenan~",
                        columns: x => new { x.TenantId, x.PayrollCompliancePeriodId },
                        principalTable: "PayrollCompliancePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollStatutoryChallans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollStatutoryReturnBatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChallanNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BankReference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ExternalReference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollStatutoryChallans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryChallans_PayrollStatutoryReturnBatches_Tenan~",
                        columns: x => new { x.TenantId, x.PayrollStatutoryReturnBatchId },
                        principalTable: "PayrollStatutoryReturnBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryChallans_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollStatutoryComplianceHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollStatutoryReturnBatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollStatutoryComplianceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryComplianceHistories_PayrollStatutoryReturnBa~",
                        columns: x => new { x.TenantId, x.PayrollStatutoryReturnBatchId },
                        principalTable: "PayrollStatutoryReturnBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryComplianceHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollStatutoryReturnEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollStatutoryReturnBatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeCodeSnapshot = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    EmployeeNameSnapshot = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Uan = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    EsicNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Pan = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    PtRegistrationReference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    GrossWages = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StatutoryWages = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeContribution = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployerContribution = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeductionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PayableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValidationStatus = table.Column<int>(type: "int", nullable: false),
                    ValidationMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollStatutoryReturnEmployees", x => x.Id);
                    table.UniqueConstraint("AK_PayrollStatutoryReturnEmployees_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnEmployees_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnEmployees_PayrollStatutoryReturnBatche~",
                        columns: x => new { x.TenantId, x.PayrollStatutoryReturnBatchId },
                        principalTable: "PayrollStatutoryReturnBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnEmployees_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollStatutoryReturnSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollStatutoryReturnBatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollStatutoryReturnEmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollResultId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollStatutoryResultId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollStatutoryReturnSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnSources_PayrollStatutoryReturnBatches_~",
                        columns: x => new { x.TenantId, x.PayrollStatutoryReturnBatchId },
                        principalTable: "PayrollStatutoryReturnBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnSources_PayrollStatutoryReturnEmployee~",
                        columns: x => new { x.TenantId, x.PayrollStatutoryReturnEmployeeId },
                        principalTable: "PayrollStatutoryReturnEmployees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryReturnSources_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCompliancePeriods_TenantId_JurisdictionCode_Complianc~",
                table: "PayrollCompliancePeriods",
                columns: new[] { "TenantId", "JurisdictionCode", "ComplianceType", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryChallans_TenantId_PayrollStatutoryReturnBatc~",
                table: "PayrollStatutoryChallans",
                columns: new[] { "TenantId", "PayrollStatutoryReturnBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryComplianceHistories_TenantId_PayrollStatutor~",
                table: "PayrollStatutoryComplianceHistories",
                columns: new[] { "TenantId", "PayrollStatutoryReturnBatchId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnBatches_TenantId_BatchNumber",
                table: "PayrollStatutoryReturnBatches",
                columns: new[] { "TenantId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnBatches_TenantId_PayrollCompliancePeri~",
                table: "PayrollStatutoryReturnBatches",
                columns: new[] { "TenantId", "PayrollCompliancePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnBatches_TenantId_Status",
                table: "PayrollStatutoryReturnBatches",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnEmployees_TenantId_EmployeeId",
                table: "PayrollStatutoryReturnEmployees",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnEmployees_TenantId_PayrollStatutoryRet~",
                table: "PayrollStatutoryReturnEmployees",
                columns: new[] { "TenantId", "PayrollStatutoryReturnBatchId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnSources_TenantId_PayrollStatutoryResul~",
                table: "PayrollStatutoryReturnSources",
                columns: new[] { "TenantId", "PayrollStatutoryResultId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnSources_TenantId_PayrollStatutoryRetu~1",
                table: "PayrollStatutoryReturnSources",
                columns: new[] { "TenantId", "PayrollStatutoryReturnEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryReturnSources_TenantId_PayrollStatutoryRetur~",
                table: "PayrollStatutoryReturnSources",
                columns: new[] { "TenantId", "PayrollStatutoryReturnBatchId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollStatutoryChallans");

            migrationBuilder.DropTable(
                name: "PayrollStatutoryComplianceHistories");

            migrationBuilder.DropTable(
                name: "PayrollStatutoryReturnSources");

            migrationBuilder.DropTable(
                name: "PayrollStatutoryReturnEmployees");

            migrationBuilder.DropTable(
                name: "PayrollStatutoryReturnBatches");

            migrationBuilder.DropTable(
                name: "PayrollCompliancePeriods");
        }
    }
}
