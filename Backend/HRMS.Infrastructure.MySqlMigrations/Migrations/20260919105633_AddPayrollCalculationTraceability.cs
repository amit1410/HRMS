using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollCalculationTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollResults_TenantId_PayrollRunId_EmployeeId",
                table: "PayrollResults");

            migrationBuilder.AddColumn<Guid>(
                name: "CalculationAttemptId",
                table: "PayrollResults",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "CalendarDays",
                table: "PayrollResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EligibleDays",
                table: "PayrollResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerContributions",
                table: "PayrollResults",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmploymentSnapshotDate",
                table: "PayrollResults",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "PayrollResults",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ProrationFactor",
                table: "PayrollResults",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CalculationAttemptId",
                table: "PayrollResultComponents",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "FormulaSnapshot",
                table: "PayrollResultComponents",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProrationFactor",
                table: "PayrollResultComponents",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnproratedAmount",
                table: "PayrollResultComponents",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CalculationAttemptId",
                table: "PayrollCalculationHistories",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CalculationAttemptId",
                table: "PayrollCalculationErrors",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "PayrollCalculationErrors",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollResults_TenantId_PayrollRunId_EmployeeId_CalculationA~",
                table: "PayrollResults",
                columns: new[] { "TenantId", "PayrollRunId", "EmployeeId", "CalculationAttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollResults_TenantId_PayrollRunId_EmployeeId_IsCurrent",
                table: "PayrollResults",
                columns: new[] { "TenantId", "PayrollRunId", "EmployeeId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollResultComponents_TenantId_CalculationAttemptId",
                table: "PayrollResultComponents",
                columns: new[] { "TenantId", "CalculationAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCalculationHistories_TenantId_PayrollRunId_Calculatio~",
                table: "PayrollCalculationHistories",
                columns: new[] { "TenantId", "PayrollRunId", "CalculationAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCalculationErrors_TenantId_PayrollRunId_CalculationAt~",
                table: "PayrollCalculationErrors",
                columns: new[] { "TenantId", "PayrollRunId", "CalculationAttemptId", "IsCurrent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollResults_TenantId_PayrollRunId_EmployeeId_CalculationA~",
                table: "PayrollResults");

            migrationBuilder.DropIndex(
                name: "IX_PayrollResults_TenantId_PayrollRunId_EmployeeId_IsCurrent",
                table: "PayrollResults");

            migrationBuilder.DropIndex(
                name: "IX_PayrollResultComponents_TenantId_CalculationAttemptId",
                table: "PayrollResultComponents");

            migrationBuilder.DropIndex(
                name: "IX_PayrollCalculationHistories_TenantId_PayrollRunId_Calculatio~",
                table: "PayrollCalculationHistories");

            migrationBuilder.DropIndex(
                name: "IX_PayrollCalculationErrors_TenantId_PayrollRunId_CalculationAt~",
                table: "PayrollCalculationErrors");

            migrationBuilder.DropColumn(
                name: "CalculationAttemptId",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "CalendarDays",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "EligibleDays",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "EmployerContributions",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "EmploymentSnapshotDate",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "ProrationFactor",
                table: "PayrollResults");

            migrationBuilder.DropColumn(
                name: "CalculationAttemptId",
                table: "PayrollResultComponents");

            migrationBuilder.DropColumn(
                name: "FormulaSnapshot",
                table: "PayrollResultComponents");

            migrationBuilder.DropColumn(
                name: "ProrationFactor",
                table: "PayrollResultComponents");

            migrationBuilder.DropColumn(
                name: "UnproratedAmount",
                table: "PayrollResultComponents");

            migrationBuilder.DropColumn(
                name: "CalculationAttemptId",
                table: "PayrollCalculationHistories");

            migrationBuilder.DropColumn(
                name: "CalculationAttemptId",
                table: "PayrollCalculationErrors");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "PayrollCalculationErrors");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollResults_TenantId_PayrollRunId_EmployeeId",
                table: "PayrollResults",
                columns: new[] { "TenantId", "PayrollRunId", "EmployeeId" },
                unique: true);
        }
    }
}
