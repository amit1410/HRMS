using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollInputsPhase7V : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollInputTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InputType = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    HeaderRow = table.Column<int>(type: "int", nullable: false),
                    Delimiter = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInputTemplates", x => x.Id);
                    table.UniqueConstraint("AK_PayrollInputTemplates_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollInputTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInputBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ValidRows = table.Column<int>(type: "int", nullable: false),
                    InvalidRows = table.Column<int>(type: "int", nullable: false),
                    WarningRows = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    FileHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInputBatches", x => x.Id);
                    table.UniqueConstraint("AK_PayrollInputBatches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollInputBatches_PayrollInputTemplates_TenantId_TemplateId",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "PayrollInputTemplates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollInputBatches_PayrollPeriods_TenantId_PayrollPeriodId",
                        columns: x => new { x.TenantId, x.PayrollPeriodId },
                        principalTable: "PayrollPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollInputBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInputTemplateColumns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceColumnName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TransformType = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInputTemplateColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollInputTemplateColumns_PayrollInputTemplates_TenantId_TemplateId",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "PayrollInputTemplates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollInputTemplateColumns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInputHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_PayrollInputHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollInputHistories_PayrollInputBatches_TenantId_BatchId",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "PayrollInputBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollInputHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInputLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SalaryComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ComponentCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InputType = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SourceRowHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInputLines", x => x.Id);
                    table.UniqueConstraint("AK_PayrollInputLines_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollInputLines_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollInputLines_PayrollInputBatches_TenantId_BatchId",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "PayrollInputBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollInputLines_SalaryComponents_TenantId_SalaryComponentId",
                        columns: x => new { x.TenantId, x.SalaryComponentId },
                        principalTable: "SalaryComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollInputLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInputValidationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowNumber = table.Column<int>(type: "int", nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInputValidationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollInputValidationIssues_PayrollInputBatches_TenantId_BatchId",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "PayrollInputBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollInputValidationIssues_PayrollInputLines_TenantId_LineId",
                        columns: x => new { x.TenantId, x.LineId },
                        principalTable: "PayrollInputLines",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_PayrollInputValidationIssues_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputBatches_TenantId_BatchNumber",
                table: "PayrollInputBatches",
                columns: new[] { "TenantId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputBatches_TenantId_FileHash_TemplateId",
                table: "PayrollInputBatches",
                columns: new[] { "TenantId", "FileHash", "TemplateId" },
                unique: true,
                filter: "[FileHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputBatches_TenantId_PayrollPeriodId",
                table: "PayrollInputBatches",
                columns: new[] { "TenantId", "PayrollPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputBatches_TenantId_TemplateId",
                table: "PayrollInputBatches",
                columns: new[] { "TenantId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputHistories_TenantId_BatchId_OccurredAtUtc",
                table: "PayrollInputHistories",
                columns: new[] { "TenantId", "BatchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputLines_TenantId_BatchId_RowNumber",
                table: "PayrollInputLines",
                columns: new[] { "TenantId", "BatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputLines_TenantId_EmployeeId",
                table: "PayrollInputLines",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputLines_TenantId_SalaryComponentId",
                table: "PayrollInputLines",
                columns: new[] { "TenantId", "SalaryComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputLines_TenantId_SourceRowHash",
                table: "PayrollInputLines",
                columns: new[] { "TenantId", "SourceRowHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputTemplateColumns_TenantId_TemplateId_Position",
                table: "PayrollInputTemplateColumns",
                columns: new[] { "TenantId", "TemplateId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputTemplates_TenantId_Code_Version",
                table: "PayrollInputTemplates",
                columns: new[] { "TenantId", "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputValidationIssues_TenantId_BatchId_Severity_RowNumber",
                table: "PayrollInputValidationIssues",
                columns: new[] { "TenantId", "BatchId", "Severity", "RowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputValidationIssues_TenantId_LineId",
                table: "PayrollInputValidationIssues",
                columns: new[] { "TenantId", "LineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollInputHistories");

            migrationBuilder.DropTable(
                name: "PayrollInputTemplateColumns");

            migrationBuilder.DropTable(
                name: "PayrollInputValidationIssues");

            migrationBuilder.DropTable(
                name: "PayrollInputLines");

            migrationBuilder.DropTable(
                name: "PayrollInputBatches");

            migrationBuilder.DropTable(
                name: "PayrollInputTemplates");
        }
    }
}
