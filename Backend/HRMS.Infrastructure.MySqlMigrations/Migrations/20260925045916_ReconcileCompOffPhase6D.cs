using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileCompOffPhase6D : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompOffLeaveAllocations_LeaveRequests_TenantId_LeaveRequestId",
                table: "CompOffLeaveAllocations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_CompOffLeaveAllocations_LeaveRequests_TenantId_LeaveRequestId",
                table: "CompOffLeaveAllocations",
                columns: new[] { "TenantId", "LeaveRequestId" },
                principalTable: "LeaveRequests",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

        }
    }
}
