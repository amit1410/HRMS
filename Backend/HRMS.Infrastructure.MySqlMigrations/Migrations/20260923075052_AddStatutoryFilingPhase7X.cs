using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStatutoryFilingPhase7X : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StatutoryFilingDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    FilingType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    JurisdictionCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    RequiresApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DestinationType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingDefinitions", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryFilingDefinitions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingDefinitions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingDefinitionVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    OutputFormat = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingDefinitionVersions", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryFilingDefinitionVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingDefinitionVersions_StatutoryFilingDefinitions~",
                        columns: x => new { x.TenantId, x.DefinitionId },
                        principalTable: "StatutoryFilingDefinitions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingDefinitionVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingFieldMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DefinitionVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OutputFieldName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    SourceField = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Required = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Format = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DefaultValue = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingFieldMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingFieldMappings_StatutoryFilingDefinitionVersio~",
                        columns: x => new { x.TenantId, x.DefinitionVersionId },
                        principalTable: "StatutoryFilingDefinitionVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingFieldMappings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DefinitionVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FilingPeriod = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ValidationErrorCount = table.Column<int>(type: "int", nullable: false),
                    ValidationWarningCount = table.Column<int>(type: "int", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GeneratedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingRuns", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryFilingRuns_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingRuns_StatutoryFilingDefinitionVersions_Tenant~",
                        columns: x => new { x.TenantId, x.DefinitionVersionId },
                        principalTable: "StatutoryFilingDefinitionVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingRuns_StatutoryFilingDefinitions_TenantId_Defi~",
                        columns: x => new { x.TenantId, x.DefinitionId },
                        principalTable: "StatutoryFilingDefinitions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Event = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingHistories_StatutoryFilingRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "StatutoryFilingRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    PackageHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<string>(type: "longtext", maxLength: 2000000, nullable: false),
                    IsSubmitted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingPackages", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryFilingPackages_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingPackages_StatutoryFilingRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "StatutoryFilingRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingPackages_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingRunItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PayrollStatutoryReturnEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceReference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    SourceSnapshotJson = table.Column<string>(type: "varchar(12000)", maxLength: 12000, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingRunItems", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryFilingRunItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingRunItems_StatutoryFilingRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "StatutoryFilingRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingRunItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PackageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ExternalReference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ResponseCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    SafeResponseSummary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingSubmissions", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryFilingSubmissions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingSubmissions_StatutoryFilingPackages_TenantId_~",
                        columns: x => new { x.TenantId, x.PackageId },
                        principalTable: "StatutoryFilingPackages",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingSubmissions_StatutoryFilingRuns_TenantId_RunId",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "StatutoryFilingRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingSubmissions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingValidationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunItemId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingValidationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingValidationIssues_StatutoryFilingRunItems_Tena~",
                        columns: x => new { x.TenantId, x.RunItemId },
                        principalTable: "StatutoryFilingRunItems",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingValidationIssues_StatutoryFilingRuns_TenantId~",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "StatutoryFilingRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingValidationIssues_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingAcknowledgements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingAcknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingAcknowledgements_StatutoryFilingSubmissions_T~",
                        columns: x => new { x.TenantId, x.SubmissionId },
                        principalTable: "StatutoryFilingSubmissions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingAcknowledgements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StatutoryFilingSubmissionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ExternalReference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ResponseCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    SafeResponseSummary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingSubmissionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingSubmissionAttempts_StatutoryFilingSubmissions~",
                        columns: x => new { x.TenantId, x.SubmissionId },
                        principalTable: "StatutoryFilingSubmissions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatutoryFilingSubmissionAttempts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingAcknowledgements_TenantId_SubmissionId_Refere~",
                table: "StatutoryFilingAcknowledgements",
                columns: new[] { "TenantId", "SubmissionId", "ReferenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingDefinitions_TenantId_Code",
                table: "StatutoryFilingDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingDefinitionVersions_TenantId_DefinitionId_Vers~",
                table: "StatutoryFilingDefinitionVersions",
                columns: new[] { "TenantId", "DefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingFieldMappings_TenantId_DefinitionVersionId_Se~",
                table: "StatutoryFilingFieldMappings",
                columns: new[] { "TenantId", "DefinitionVersionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingHistories_TenantId_RunId_OccurredAtUtc",
                table: "StatutoryFilingHistories",
                columns: new[] { "TenantId", "RunId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingPackages_TenantId_PackageHash",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "PackageHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingPackages_TenantId_RunId_Version",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "RunId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingRunItems_TenantId_RunId_SourceReference",
                table: "StatutoryFilingRunItems",
                columns: new[] { "TenantId", "RunId", "SourceReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingRuns_TenantId_DefinitionId_FilingPeriod_Status",
                table: "StatutoryFilingRuns",
                columns: new[] { "TenantId", "DefinitionId", "FilingPeriod", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingRuns_TenantId_DefinitionVersionId",
                table: "StatutoryFilingRuns",
                columns: new[] { "TenantId", "DefinitionVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingSubmissionAttempts_TenantId_SubmissionId_Atte~",
                table: "StatutoryFilingSubmissionAttempts",
                columns: new[] { "TenantId", "SubmissionId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingSubmissions_TenantId_PackageId",
                table: "StatutoryFilingSubmissions",
                columns: new[] { "TenantId", "PackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingSubmissions_TenantId_RunId",
                table: "StatutoryFilingSubmissions",
                columns: new[] { "TenantId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingValidationIssues_TenantId_RunId_Code",
                table: "StatutoryFilingValidationIssues",
                columns: new[] { "TenantId", "RunId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingValidationIssues_TenantId_RunItemId",
                table: "StatutoryFilingValidationIssues",
                columns: new[] { "TenantId", "RunItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatutoryFilingAcknowledgements");

            migrationBuilder.DropTable(
                name: "StatutoryFilingFieldMappings");

            migrationBuilder.DropTable(
                name: "StatutoryFilingHistories");

            migrationBuilder.DropTable(
                name: "StatutoryFilingSubmissionAttempts");

            migrationBuilder.DropTable(
                name: "StatutoryFilingValidationIssues");

            migrationBuilder.DropTable(
                name: "StatutoryFilingSubmissions");

            migrationBuilder.DropTable(
                name: "StatutoryFilingRunItems");

            migrationBuilder.DropTable(
                name: "StatutoryFilingPackages");

            migrationBuilder.DropTable(
                name: "StatutoryFilingRuns");

            migrationBuilder.DropTable(
                name: "StatutoryFilingDefinitionVersions");

            migrationBuilder.DropTable(
                name: "StatutoryFilingDefinitions");
        }
    }
}
