using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanRepaymentInstallmentLinkSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LoanInstallmentId",
                table: "LoanRepayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_LoanInstallments_TenantId_Id",
                table: "LoanInstallments",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanRepayments_TenantId_LoanInstallmentId",
                table: "LoanRepayments",
                columns: new[] { "TenantId", "LoanInstallmentId" },
                unique: true,
                filter: "[LoanInstallmentId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_LoanRepayments_LoanInstallments_TenantId_LoanInstallmentId",
                table: "LoanRepayments",
                columns: new[] { "TenantId", "LoanInstallmentId" },
                principalTable: "LoanInstallments",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanRepayments_LoanInstallments_TenantId_LoanInstallmentId",
                table: "LoanRepayments");

            migrationBuilder.DropIndex(
                name: "IX_LoanRepayments_TenantId_LoanInstallmentId",
                table: "LoanRepayments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_LoanInstallments_TenantId_Id",
                table: "LoanInstallments");

            migrationBuilder.DropColumn(
                name: "LoanInstallmentId",
                table: "LoanRepayments");
        }
    }
}
