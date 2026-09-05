using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

public partial class LeaveBalanceLedgerFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmployeeLeaveBalances",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeavePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GrantedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                ReservedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                ConsumedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmployeeLeaveBalances", x => x.Id);
                table.UniqueConstraint("AK_EmployeeLeaveBalances_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_EmployeeLeaveBalances_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EmployeeLeaveBalances_Employees_TenantId_EmployeeId", x => new { x.TenantId, x.EmployeeId }, "Employees", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EmployeeLeaveBalances_LeaveTypes_TenantId_LeaveTypeId", x => new { x.TenantId, x.LeaveTypeId }, "LeaveTypes", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EmployeeLeaveBalances_LeavePeriods_TenantId_LeavePeriodId", x => new { x.TenantId, x.LeavePeriodId }, "LeavePeriods", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_EmployeeLeaveBalances_NonNegativeAndAvailable", "[GrantedQuantity] >= 0 AND [ReservedQuantity] >= 0 AND [ConsumedQuantity] >= 0 AND [ReservedQuantity] + [ConsumedQuantity] <= [GrantedQuantity]");
            });

        migrationBuilder.CreateIndex("IX_EmployeeLeaveBalances_TenantId_EmployeeId_LeaveTypeId_LeavePeriodId", "EmployeeLeaveBalances", new[] { "TenantId", "EmployeeId", "LeaveTypeId", "LeavePeriodId" }, unique: true);
        migrationBuilder.CreateIndex("IX_EmployeeLeaveBalances_TenantId_LeaveTypeId", "EmployeeLeaveBalances", new[] { "TenantId", "LeaveTypeId" });
        migrationBuilder.CreateIndex("IX_EmployeeLeaveBalances_TenantId_LeavePeriodId", "EmployeeLeaveBalances", new[] { "TenantId", "LeavePeriodId" });

        migrationBuilder.CreateTable(
            name: "LeaveBalanceTransactions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EmployeeLeaveBalanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeavePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TransactionType = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                LeavePolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SourceType = table.Column<int>(type: "int", nullable: false),
                SourceReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                ActorType = table.Column<int>(type: "int", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ActorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                PayloadFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LeaveBalanceTransactions", x => x.Id);
                table.UniqueConstraint("AK_LeaveBalanceTransactions_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_LeaveBalanceTransactions_EmployeeLeaveBalances_TenantId_EmployeeLeaveBalanceId", x => new { x.TenantId, x.EmployeeLeaveBalanceId }, "EmployeeLeaveBalances", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LeaveBalanceTransactions_Employees_TenantId_EmployeeId", x => new { x.TenantId, x.EmployeeId }, "Employees", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LeaveBalanceTransactions_LeaveTypes_TenantId_LeaveTypeId", x => new { x.TenantId, x.LeaveTypeId }, "LeaveTypes", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LeaveBalanceTransactions_LeavePeriods_TenantId_LeavePeriodId", x => new { x.TenantId, x.LeavePeriodId }, "LeavePeriods", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LeaveBalanceTransactions_LeavePolicyVersions_TenantId_LeavePolicyVersionId", x => new { x.TenantId, x.LeavePolicyVersionId }, "LeavePolicyVersions", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LeaveBalanceTransactions_LeavePolicyRules_TenantId_LeavePolicyRuleId", x => new { x.TenantId, x.LeavePolicyRuleId }, "LeavePolicyRules", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LeaveBalanceTransactions_Users_TenantId_ActorUserId", x => new { x.TenantId, x.ActorUserId }, "Users", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LeaveBalanceTransactions_Employees_TenantId_ActorEmployeeId", x => new { x.TenantId, x.ActorEmployeeId }, "Employees", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_LeaveBalanceTransactions_PositiveQuantity", "[Quantity] > 0");
            });

        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_IdempotencyKey", "LeaveBalanceTransactions", new[] { "TenantId", "IdempotencyKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_EmployeeId_LeaveTypeId_LeavePeriodId_EffectiveDate", "LeaveBalanceTransactions", new[] { "TenantId", "EmployeeId", "LeaveTypeId", "LeavePeriodId", "EffectiveDate" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_EmployeeLeaveBalanceId", "LeaveBalanceTransactions", new[] { "TenantId", "EmployeeLeaveBalanceId" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_EmployeeId", "LeaveBalanceTransactions", new[] { "TenantId", "EmployeeId" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_LeaveTypeId", "LeaveBalanceTransactions", new[] { "TenantId", "LeaveTypeId" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_LeavePeriodId", "LeaveBalanceTransactions", new[] { "TenantId", "LeavePeriodId" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_LeavePolicyVersionId", "LeaveBalanceTransactions", new[] { "TenantId", "LeavePolicyVersionId" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_LeavePolicyRuleId", "LeaveBalanceTransactions", new[] { "TenantId", "LeavePolicyRuleId" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_ActorUserId", "LeaveBalanceTransactions", new[] { "TenantId", "ActorUserId" });
        migrationBuilder.CreateIndex("IX_LeaveBalanceTransactions_TenantId_ActorEmployeeId", "LeaveBalanceTransactions", new[] { "TenantId", "ActorEmployeeId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("LeaveBalanceTransactions");
        migrationBuilder.DropTable("EmployeeLeaveBalances");
    }
}
