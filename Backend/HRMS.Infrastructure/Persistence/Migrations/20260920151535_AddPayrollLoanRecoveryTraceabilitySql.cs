using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollLoanRecoveryTraceabilitySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SalaryComponentId",
                table: "PayrollResultComponents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeLoanId",
                table: "PayrollResultComponents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LoanInstallmentId",
                table: "PayrollResultComponents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollResultComponents_TenantId_EmployeeLoanId_LoanInstallmentId",
                table: "PayrollResultComponents",
                columns: new[] { "TenantId", "EmployeeLoanId", "LoanInstallmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollResultComponents_TenantId_LoanInstallmentId",
                table: "PayrollResultComponents",
                columns: new[] { "TenantId", "LoanInstallmentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollResultComponents_EmployeeLoans_TenantId_EmployeeLoanId",
                table: "PayrollResultComponents",
                columns: new[] { "TenantId", "EmployeeLoanId" },
                principalTable: "EmployeeLoans",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollResultComponents_LoanInstallments_TenantId_LoanInstallmentId",
                table: "PayrollResultComponents",
                columns: new[] { "TenantId", "LoanInstallmentId" },
                principalTable: "LoanInstallments",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollResultComponents_EmployeeLoans_TenantId_EmployeeLoanId",
                table: "PayrollResultComponents");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollResultComponents_LoanInstallments_TenantId_LoanInstallmentId",
                table: "PayrollResultComponents");

            migrationBuilder.DropIndex(
                name: "IX_PayrollResultComponents_TenantId_EmployeeLoanId_LoanInstallmentId",
                table: "PayrollResultComponents");

            migrationBuilder.DropIndex(
                name: "IX_PayrollResultComponents_TenantId_LoanInstallmentId",
                table: "PayrollResultComponents");

            migrationBuilder.DropColumn(
                name: "EmployeeLoanId",
                table: "PayrollResultComponents");

            migrationBuilder.DropColumn(
                name: "LoanInstallmentId",
                table: "PayrollResultComponents");

            migrationBuilder.AlterColumn<Guid>(
                name: "SalaryComponentId",
                table: "PayrollResultComponents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
