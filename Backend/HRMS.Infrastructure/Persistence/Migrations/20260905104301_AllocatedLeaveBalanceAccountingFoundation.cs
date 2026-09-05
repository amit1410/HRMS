using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllocatedLeaveBalanceAccountingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaveRequestId",
                table: "LeaveBalanceTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_LeaveRequestId_TransactionType",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "LeaveRequestId", "TransactionType" },
                unique: true,
                filter: "[LeaveRequestId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveBalanceTransactions_LeaveRequests_TenantId_LeaveRequestId",
                table: "LeaveBalanceTransactions",
                columns: new[] { "TenantId", "LeaveRequestId" },
                principalTable: "LeaveRequests",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaveBalanceTransactions_LeaveRequests_TenantId_LeaveRequestId",
                table: "LeaveBalanceTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LeaveBalanceTransactions_TenantId_LeaveRequestId_TransactionType",
                table: "LeaveBalanceTransactions");

            migrationBuilder.DropColumn(
                name: "LeaveRequestId",
                table: "LeaveBalanceTransactions");
        }
    }
}
