using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeCompOffPhase6D : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompOffEarnings_Tenants_TenantId1",
                table: "CompOffEarnings");

            migrationBuilder.DropForeignKey(
                name: "FK_CompOffLeaveAllocations_Tenants_TenantId1",
                table: "CompOffLeaveAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_CompOffLedgerEntries_Tenants_TenantId1",
                table: "CompOffLedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_CompOffPolicies_Tenants_TenantId1",
                table: "CompOffPolicies");

            migrationBuilder.DropIndex(
                name: "IX_CompOffPolicies_TenantId1",
                table: "CompOffPolicies");

            migrationBuilder.DropIndex(
                name: "IX_CompOffLedgerEntries_TenantId1",
                table: "CompOffLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_CompOffLeaveAllocations_TenantId1",
                table: "CompOffLeaveAllocations");

            migrationBuilder.DropIndex(
                name: "IX_CompOffEarnings_TenantId1",
                table: "CompOffEarnings");

            migrationBuilder.DropColumn(
                name: "TenantId1",
                table: "CompOffPolicies");

            migrationBuilder.DropColumn(
                name: "TenantId1",
                table: "CompOffLedgerEntries");

            migrationBuilder.DropColumn(
                name: "TenantId1",
                table: "CompOffLeaveAllocations");

            migrationBuilder.DropColumn(
                name: "TenantId1",
                table: "CompOffEarnings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId1",
                table: "CompOffPolicies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId1",
                table: "CompOffLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId1",
                table: "CompOffLeaveAllocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId1",
                table: "CompOffEarnings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompOffPolicies_TenantId1",
                table: "CompOffPolicies",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLedgerEntries_TenantId1",
                table: "CompOffLedgerEntries",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLeaveAllocations_TenantId1",
                table: "CompOffLeaveAllocations",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_CompOffEarnings_TenantId1",
                table: "CompOffEarnings",
                column: "TenantId1");

            migrationBuilder.AddForeignKey(
                name: "FK_CompOffEarnings_Tenants_TenantId1",
                table: "CompOffEarnings",
                column: "TenantId1",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CompOffLeaveAllocations_Tenants_TenantId1",
                table: "CompOffLeaveAllocations",
                column: "TenantId1",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CompOffLedgerEntries_Tenants_TenantId1",
                table: "CompOffLedgerEntries",
                column: "TenantId1",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CompOffPolicies_Tenants_TenantId1",
                table: "CompOffPolicies",
                column: "TenantId1",
                principalTable: "Tenants",
                principalColumn: "Id");
        }
    }
}
