using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAdjustmentFinalSettlementLinkPhase7RModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollAdjustmentId_PayrollRunId",
                table: "PayrollAdjustmentApplications");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollRunId",
                table: "PayrollAdjustmentApplications",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollResultId",
                table: "PayrollAdjustmentApplications",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "FinalSettlementId",
                table: "PayrollAdjustmentApplications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_FinalSettlementId",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "FinalSettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollAdjustmentId_FinalSettlementId",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "PayrollAdjustmentId", "FinalSettlementId" },
                unique: true,
                filter: "[FinalSettlementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollAdjustmentId_PayrollRunId",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "PayrollAdjustmentId", "PayrollRunId" },
                unique: true,
                filter: "[PayrollRunId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollAdjustmentApplications_FinalSettlementCases_TenantId_FinalSettlementId",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "FinalSettlementId" },
                principalTable: "FinalSettlementCases",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollAdjustmentApplications_FinalSettlementCases_TenantId_FinalSettlementId",
                table: "PayrollAdjustmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_FinalSettlementId",
                table: "PayrollAdjustmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollAdjustmentId_FinalSettlementId",
                table: "PayrollAdjustmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollAdjustmentId_PayrollRunId",
                table: "PayrollAdjustmentApplications");

            migrationBuilder.DropColumn(
                name: "FinalSettlementId",
                table: "PayrollAdjustmentApplications");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollRunId",
                table: "PayrollAdjustmentApplications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PayrollResultId",
                table: "PayrollAdjustmentApplications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustmentApplications_TenantId_PayrollAdjustmentId_PayrollRunId",
                table: "PayrollAdjustmentApplications",
                columns: new[] { "TenantId", "PayrollAdjustmentId", "PayrollRunId" },
                unique: true);
        }
    }
}
