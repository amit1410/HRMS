using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalPayrollDimensionsPhase7S : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "PayrollRunEmployees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "PayrollRunEmployees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_TenantId_PayrollRunId_DepartmentId",
                table: "PayrollRunEmployees",
                columns: new[] { "TenantId", "PayrollRunId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_TenantId_PayrollRunId_CostCenterId",
                table: "PayrollRunEmployees",
                columns: new[] { "TenantId", "PayrollRunId", "CostCenterId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRunEmployees_TenantId_PayrollRunId_DepartmentId",
                table: "PayrollRunEmployees");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRunEmployees_TenantId_PayrollRunId_CostCenterId",
                table: "PayrollRunEmployees");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "PayrollRunEmployees");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "PayrollRunEmployees");
        }
    }
}
