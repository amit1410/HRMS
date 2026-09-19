using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStatutoryCalculationFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeStatutoryProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    StateCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    PfApplicable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Uan = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    EsiApplicable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EsiNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    ProfessionalTaxApplicable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IncomeTaxApplicable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TaxRegime = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeStatutoryProfiles", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeStatutoryProfiles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeStatutoryProfiles_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeStatutoryProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    StateCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    StatutoryType = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryConfigurations", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryConfigurations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryConfigurations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeStatutoryProfileHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeStatutoryProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", maxLength: 20000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeStatutoryProfileHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeStatutoryProfileHistories_EmployeeStatutoryProfiles_~",
                        columns: x => new { x.TenantId, x.EmployeeStatutoryProfileId },
                        principalTable: "EmployeeStatutoryProfiles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeStatutoryProfileHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryConfigurationHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StatutoryConfigurationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", maxLength: 20000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryConfigurationHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryConfigurationHistories_StatutoryConfigurations_Tena~",
                        columns: x => new { x.TenantId, x.StatutoryConfigurationId },
                        principalTable: "StatutoryConfigurations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryConfigurationHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryConfigurationVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StatutoryConfigurationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "longtext", maxLength: 20000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryConfigurationVersions", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryConfigurationVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryConfigurationVersions_StatutoryConfigurations_Tenan~",
                        columns: x => new { x.TenantId, x.StatutoryConfigurationId },
                        principalTable: "StatutoryConfigurations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryConfigurationVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryConfigurationVersions_Users_TenantId_CreatedByUserId",
                        columns: x => new { x.TenantId, x.CreatedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollStatutoryResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollResultId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StatutoryType = table.Column<int>(type: "int", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    StatutoryConfigurationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StatutoryConfigurationVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CalculationBasis = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EmployeeAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EmployerAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AppliedRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    AppliedCeiling = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CalculationMetadata = table.Column<string>(type: "longtext", maxLength: 20000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollStatutoryResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryResults_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryResults_PayrollResults_TenantId_PayrollResul~",
                        columns: x => new { x.TenantId, x.PayrollResultId },
                        principalTable: "PayrollResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryResults_PayrollRuns_TenantId_PayrollRunId",
                        columns: x => new { x.TenantId, x.PayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryResults_StatutoryConfigurationVersions_Tenan~",
                        columns: x => new { x.TenantId, x.StatutoryConfigurationVersionId },
                        principalTable: "StatutoryConfigurationVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryResults_StatutoryConfigurations_TenantId_Sta~",
                        columns: x => new { x.TenantId, x.StatutoryConfigurationId },
                        principalTable: "StatutoryConfigurations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatutoryResults_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryComponentBasis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StatutoryConfigurationVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryComponentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Include = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryComponentBasis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryComponentBasis_SalaryComponents_TenantId_SalaryComp~",
                        columns: x => new { x.TenantId, x.SalaryComponentId },
                        principalTable: "SalaryComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryComponentBasis_StatutoryConfigurationVersions_Tenan~",
                        columns: x => new { x.TenantId, x.StatutoryConfigurationVersionId },
                        principalTable: "StatutoryConfigurationVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryComponentBasis_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutorySlabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StatutoryConfigurationVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OptionalMonth = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutorySlabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutorySlabs_StatutoryConfigurationVersions_TenantId_Statu~",
                        columns: x => new { x.TenantId, x.StatutoryConfigurationVersionId },
                        principalTable: "StatutoryConfigurationVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutorySlabs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeStatutoryProfileHistories_TenantId_EmployeeStatutory~",
                table: "EmployeeStatutoryProfileHistories",
                columns: new[] { "TenantId", "EmployeeStatutoryProfileId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeStatutoryProfiles_TenantId_EmployeeId_EffectiveFrom_~",
                table: "EmployeeStatutoryProfiles",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryResults_TenantId_EmployeeId",
                table: "PayrollStatutoryResults",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryResults_TenantId_PayrollResultId_StatutoryTy~",
                table: "PayrollStatutoryResults",
                columns: new[] { "TenantId", "PayrollResultId", "StatutoryType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryResults_TenantId_PayrollRunId_EmployeeId",
                table: "PayrollStatutoryResults",
                columns: new[] { "TenantId", "PayrollRunId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryResults_TenantId_StatutoryConfigurationId",
                table: "PayrollStatutoryResults",
                columns: new[] { "TenantId", "StatutoryConfigurationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatutoryResults_TenantId_StatutoryConfigurationVersi~",
                table: "PayrollStatutoryResults",
                columns: new[] { "TenantId", "StatutoryConfigurationVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryComponentBasis_TenantId_SalaryComponentId",
                table: "StatutoryComponentBasis",
                columns: new[] { "TenantId", "SalaryComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryComponentBasis_TenantId_StatutoryConfigurationVersi~",
                table: "StatutoryComponentBasis",
                columns: new[] { "TenantId", "StatutoryConfigurationVersionId", "SalaryComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryConfigurationHistories_TenantId_StatutoryConfigurat~",
                table: "StatutoryConfigurationHistories",
                columns: new[] { "TenantId", "StatutoryConfigurationId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryConfigurations_TenantId_Code",
                table: "StatutoryConfigurations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryConfigurations_TenantId_JurisdictionCode_StateCode_~",
                table: "StatutoryConfigurations",
                columns: new[] { "TenantId", "JurisdictionCode", "StateCode", "StatutoryType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryConfigurationVersions_TenantId_CreatedByUserId",
                table: "StatutoryConfigurationVersions",
                columns: new[] { "TenantId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryConfigurationVersions_TenantId_StatutoryConfigurati~",
                table: "StatutoryConfigurationVersions",
                columns: new[] { "TenantId", "StatutoryConfigurationId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutorySlabs_TenantId_StatutoryConfigurationVersionId_Sequ~",
                table: "StatutorySlabs",
                columns: new[] { "TenantId", "StatutoryConfigurationVersionId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeStatutoryProfileHistories");

            migrationBuilder.DropTable(
                name: "PayrollStatutoryResults");

            migrationBuilder.DropTable(
                name: "StatutoryComponentBasis");

            migrationBuilder.DropTable(
                name: "StatutoryConfigurationHistories");

            migrationBuilder.DropTable(
                name: "StatutorySlabs");

            migrationBuilder.DropTable(
                name: "EmployeeStatutoryProfiles");

            migrationBuilder.DropTable(
                name: "StatutoryConfigurationVersions");

            migrationBuilder.DropTable(
                name: "StatutoryConfigurations");
        }
    }
}
