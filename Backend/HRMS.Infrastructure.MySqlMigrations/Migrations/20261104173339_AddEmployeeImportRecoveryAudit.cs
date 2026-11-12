using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeImportRecoveryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeImportRecoveryAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OperationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RecoveryAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Operator = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    BeforeTotalRows = table.Column<int>(type: "int", nullable: false),
                    BeforeSucceededRows = table.Column<int>(type: "int", nullable: false),
                    BeforeFailedRows = table.Column<int>(type: "int", nullable: false),
                    BeforeSkippedRows = table.Column<int>(type: "int", nullable: false),
                    BeforePendingRows = table.Column<int>(type: "int", nullable: false),
                    AfterTotalRows = table.Column<int>(type: "int", nullable: false),
                    AfterSucceededRows = table.Column<int>(type: "int", nullable: false),
                    AfterFailedRows = table.Column<int>(type: "int", nullable: false),
                    AfterSkippedRows = table.Column<int>(type: "int", nullable: false),
                    AfterPendingRows = table.Column<int>(type: "int", nullable: false),
                    SanitizedErrorCode = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeImportRecoveryAudits", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeImportRecoveryAudits_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeImportRecoveryAudits_EmployeeImportOperations_Tenant~",
                        columns: x => new { x.TenantId, x.OperationId },
                        principalTable: "EmployeeImportOperations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeImportRecoveryAudits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportRecoveryAudits_TenantId_OperationId_RecoveryAt~",
                table: "EmployeeImportRecoveryAudits",
                columns: new[] { "TenantId", "OperationId", "RecoveryAtUtc" });

            migrationBuilder.Sql("CREATE TRIGGER TR_EmployeeImportRecoveryAudits_NoUpdate BEFORE UPDATE ON EmployeeImportRecoveryAudits FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Employee import recovery audits are append-only';");
            migrationBuilder.Sql("CREATE TRIGGER TR_EmployeeImportRecoveryAudits_NoDelete BEFORE DELETE ON EmployeeImportRecoveryAudits FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Employee import recovery audits are append-only';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_EmployeeImportRecoveryAudits_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_EmployeeImportRecoveryAudits_NoDelete;");
            migrationBuilder.DropTable(
                name: "EmployeeImportRecoveryAudits");
        }
    }
}
