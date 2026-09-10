using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveEntitlementGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaveEntitlementGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeLeaveBalanceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveBalanceTransactionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceReference = table.Column<string>(type: "varchar(220)", maxLength: 220, nullable: false),
                    GrantedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ExpiredQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    GrantedOn = table.Column<DateTime>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveEntitlementGrants", x => x.Id);
                    table.UniqueConstraint("AK_LeaveEntitlementGrants_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveEntitlementGrants_EmployeeLeaveBalances_TenantId_Employ~",
                        columns: x => new { x.TenantId, x.EmployeeLeaveBalanceId },
                        principalTable: "EmployeeLeaveBalances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveEntitlementGrants_LeaveBalanceTransactions_TenantId_Lea~",
                        columns: x => new { x.TenantId, x.LeaveBalanceTransactionId },
                        principalTable: "LeaveBalanceTransactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveBalanceReservationAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveEntitlementGrantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveBalanceReservationAllocations", x => x.Id);
                    table.UniqueConstraint("AK_LeaveBalanceReservationAllocations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveBalanceReservationAllocations_LeaveEntitlementGrants_Te~",
                        columns: x => new { x.TenantId, x.LeaveEntitlementGrantId },
                        principalTable: "LeaveEntitlementGrants",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceReservationAllocations_LeaveRequests_TenantId_Le~",
                        columns: x => new { x.TenantId, x.LeaveRequestId },
                        principalTable: "LeaveRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceReservationAllocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceReservationAllocations_TenantId_LeaveEntitlement~",
                table: "LeaveBalanceReservationAllocations",
                columns: new[] { "TenantId", "LeaveEntitlementGrantId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceReservationAllocations_TenantId_LeaveRequestId_S~",
                table: "LeaveBalanceReservationAllocations",
                columns: new[] { "TenantId", "LeaveRequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlementGrants_TenantId_EmployeeId_LeaveTypeId_Leav~1",
                table: "LeaveEntitlementGrants",
                columns: new[] { "TenantId", "EmployeeId", "LeaveTypeId", "LeavePeriodId", "SourceType", "SourceReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlementGrants_TenantId_EmployeeId_LeaveTypeId_Leave~",
                table: "LeaveEntitlementGrants",
                columns: new[] { "TenantId", "EmployeeId", "LeaveTypeId", "LeavePeriodId", "ExpiresOn", "GrantedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlementGrants_TenantId_EmployeeLeaveBalanceId",
                table: "LeaveEntitlementGrants",
                columns: new[] { "TenantId", "EmployeeLeaveBalanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlementGrants_TenantId_LeaveBalanceTransactionId",
                table: "LeaveEntitlementGrants",
                columns: new[] { "TenantId", "LeaveBalanceTransactionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveBalanceReservationAllocations");

            migrationBuilder.DropTable(
                name: "LeaveEntitlementGrants");
        }
    }
}
