using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationDocumentsPhase8G : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeparationDocumentNumberSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationDocumentNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationDocumentNumberSequences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationDocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationDocumentTemplates", x => x.Id);
                    table.UniqueConstraint("AK_SeparationDocumentTemplates_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationDocumentTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationDocumentTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BodyTemplate = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: false),
                    HeaderTemplate = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    FooterTemplate = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    PageSettingsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationDocumentTemplateVersions", x => x.Id);
                    table.UniqueConstraint("AK_SeparationDocumentTemplateVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationDocumentTemplateVersions_SeparationDocumentTemplates_TenantId_TemplateId",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "SeparationDocumentTemplates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationDocumentTemplateVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationGeneratedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeSeparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    CustomDocumentCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersedesDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededByDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StorageReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: false),
                    ContentBase64 = table.Column<string>(type: "nvarchar(max)", maxLength: 2000000, nullable: false),
                    LastReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationGeneratedDocuments", x => x.Id);
                    table.UniqueConstraint("AK_SeparationGeneratedDocuments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationGeneratedDocuments_EmployeeSeparations_TenantId_EmployeeSeparationId",
                        columns: x => new { x.TenantId, x.EmployeeSeparationId },
                        principalTable: "EmployeeSeparations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationGeneratedDocuments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationGeneratedDocuments_SeparationDocumentTemplateVersions_TenantId_TemplateVersionId",
                        columns: x => new { x.TenantId, x.TemplateVersionId },
                        principalTable: "SeparationDocumentTemplateVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationGeneratedDocuments_SeparationGeneratedDocuments_SupersededByDocumentId",
                        column: x => x.SupersededByDocumentId,
                        principalTable: "SeparationGeneratedDocuments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SeparationGeneratedDocuments_SeparationGeneratedDocuments_TenantId_SupersedesDocumentId",
                        columns: x => new { x.TenantId, x.SupersedesDocumentId },
                        principalTable: "SeparationGeneratedDocuments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationGeneratedDocuments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationDocumentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GeneratedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationDocumentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationDocumentEvents_SeparationGeneratedDocuments_TenantId_GeneratedDocumentId",
                        columns: x => new { x.TenantId, x.GeneratedDocumentId },
                        principalTable: "SeparationGeneratedDocuments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationDocumentEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationDocumentEvents_TenantId_GeneratedDocumentId_OccurredAtUtc",
                table: "SeparationDocumentEvents",
                columns: new[] { "TenantId", "GeneratedDocumentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationDocumentNumberSequences_TenantId_DocumentType_Year",
                table: "SeparationDocumentNumberSequences",
                columns: new[] { "TenantId", "DocumentType", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationDocumentTemplates_TenantId_Code",
                table: "SeparationDocumentTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationDocumentTemplateVersions_TenantId_TemplateId_EffectiveFrom",
                table: "SeparationDocumentTemplateVersions",
                columns: new[] { "TenantId", "TemplateId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationDocumentTemplateVersions_TenantId_TemplateId_VersionNumber",
                table: "SeparationDocumentTemplateVersions",
                columns: new[] { "TenantId", "TemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_SupersededByDocumentId",
                table: "SeparationGeneratedDocuments",
                column: "SupersededByDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_DocumentNumber",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeId",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_DocumentType_Status",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "EmployeeSeparationId", "DocumentType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_SupersedesDocumentId",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "SupersedesDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_TemplateVersionId",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "TemplateVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeparationDocumentEvents");

            migrationBuilder.DropTable(
                name: "SeparationDocumentNumberSequences");

            migrationBuilder.DropTable(
                name: "SeparationGeneratedDocuments");

            migrationBuilder.DropTable(
                name: "SeparationDocumentTemplateVersions");

            migrationBuilder.DropTable(
                name: "SeparationDocumentTemplates");
        }
    }
}
