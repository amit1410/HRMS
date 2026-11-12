using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeImportClaimLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimLeaseExpiresAtUtc",
                table: "EmployeeImportOperations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClaimLeaseId",
                table: "EmployeeImportOperations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeImportOperations_TenantId_State_ClaimLeaseExpiresAtUtc",
                table: "EmployeeImportOperations",
                columns: new[] { "TenantId", "State", "ClaimLeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeImportOperations_TenantId_State_ClaimLeaseExpiresAtUtc",
                table: "EmployeeImportOperations");

            migrationBuilder.DropColumn(
                name: "ClaimLeaseExpiresAtUtc",
                table: "EmployeeImportOperations");

            migrationBuilder.DropColumn(
                name: "ClaimLeaseId",
                table: "EmployeeImportOperations");
        }
    }
}
