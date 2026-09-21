using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalSettlementAccountingJournalMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollRunId",
                table: "PayrollJournalBatches",
                type: "char(36)",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "char(36)");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollPeriodId",
                table: "PayrollJournalBatches",
                type: "char(36)",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "char(36)");

            migrationBuilder.AddColumn<Guid>(
                name: "FinalSettlementCaseId",
                table: "PayrollJournalBatches",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJournalBatches_TenantId_FinalSettlementCaseId",
                table: "PayrollJournalBatches",
                columns: new[] { "TenantId", "FinalSettlementCaseId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollJournalBatches_FinalSettlementCases_TenantId_FinalSet~",
                table: "PayrollJournalBatches",
                columns: new[] { "TenantId", "FinalSettlementCaseId" },
                principalTable: "FinalSettlementCases",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollJournalBatches_FinalSettlementCases_TenantId_FinalSet~",
                table: "PayrollJournalBatches");

            migrationBuilder.DropIndex(
                name: "IX_PayrollJournalBatches_TenantId_FinalSettlementCaseId",
                table: "PayrollJournalBatches");

            migrationBuilder.DropColumn(
                name: "FinalSettlementCaseId",
                table: "PayrollJournalBatches");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollRunId",
                table: "PayrollJournalBatches",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollPeriodId",
                table: "PayrollJournalBatches",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true);
        }
    }
}
