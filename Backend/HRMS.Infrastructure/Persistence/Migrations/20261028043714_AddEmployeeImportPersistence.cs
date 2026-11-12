using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeImportPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ImportBatches_TenantId_Id",
                table: "ImportBatches",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "EmployeeImportOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkbookSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ConfirmationTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    InitiatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    SucceededRows = table.Column<int>(type: "int", nullable: false),
                    FailedRows = table.Column<int>(type: "int", nullable: false),
                    SkippedRows = table.Column<int>(type: "int", nullable: false),
                    SanitizedSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeImportOperations", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeImportOperations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeImportOperations_ImportBatches_TenantId_ImportBatchId",
                        columns: x => new { x.TenantId, x.ImportBatchId },
                        principalTable: "ImportBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeImportOperations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeImportOperations_Users_TenantId_InitiatedByUserId",
                        columns: x => new { x.TenantId, x.InitiatedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExcelRowNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SanitizedErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProcessingStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeImportRows", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeImportRows_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeImportRows_EmployeeImportOperations_TenantId_OperationId",
                        columns: x => new { x.TenantId, x.OperationId },
                        principalTable: "EmployeeImportOperations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeImportRows_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportOperations_ImportBatchId",
                table: "EmployeeImportOperations",
                column: "ImportBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportOperations_TenantId_ConfirmationTokenHash",
                table: "EmployeeImportOperations",
                columns: new[] { "TenantId", "ConfirmationTokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportOperations_TenantId_ImportBatchId",
                table: "EmployeeImportOperations",
                columns: new[] { "TenantId", "ImportBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportOperations_TenantId_InitiatedByUserId",
                table: "EmployeeImportOperations",
                columns: new[] { "TenantId", "InitiatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportOperations_TenantId_State_PreparedAtUtc",
                table: "EmployeeImportOperations",
                columns: new[] { "TenantId", "State", "PreparedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportOperations_TenantId_WorkbookSha256",
                table: "EmployeeImportOperations",
                columns: new[] { "TenantId", "WorkbookSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportRows_TenantId_OperationId_ExcelRowNumber",
                table: "EmployeeImportRows",
                columns: new[] { "TenantId", "OperationId", "ExcelRowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportRows_TenantId_OperationId_Status",
                table: "EmployeeImportRows",
                columns: new[] { "TenantId", "OperationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeImportRows");

            migrationBuilder.DropTable(
                name: "EmployeeImportOperations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ImportBatches_TenantId_Id",
                table: "ImportBatches");

        }
    }
}
