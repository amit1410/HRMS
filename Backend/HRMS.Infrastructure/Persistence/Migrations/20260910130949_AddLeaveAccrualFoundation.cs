using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveAccrualFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CarryForwardEnabled",
                table: "LeavePolicyEntitlementRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CarryForwardExpiryDays",
                table: "LeavePolicyEntitlementRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumAccumulation",
                table: "LeavePolicyEntitlementRules",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumCarryForwardQuantity",
                table: "LeavePolicyEntitlementRules",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProratePartialPeriod",
                table: "LeavePolicyEntitlementRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LeaveAccrualOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccrualFrequency = table.Column<int>(type: "int", nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurrenceKey = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    CalculatedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CreditedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClaimToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveAccrualOccurrences", x => x.Id);
                    table.UniqueConstraint("AK_LeaveAccrualOccurrences_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveAccrualOccurrences_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveAccrualOccurrences_LeavePeriods_TenantId_LeavePeriodId",
                        columns: x => new { x.TenantId, x.LeavePeriodId },
                        principalTable: "LeavePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveAccrualOccurrences_LeavePolicyRules_TenantId_LeavePolicyRuleId",
                        columns: x => new { x.TenantId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveAccrualOccurrences_LeavePolicyVersions_TenantId_LeavePolicyVersionId",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId },
                        principalTable: "LeavePolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveAccrualOccurrences_LeaveTypes_TenantId_LeaveTypeId",
                        columns: x => new { x.TenantId, x.LeaveTypeId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveAccrualOccurrences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAccrualOccurrences_TenantId_EmployeeId",
                table: "LeaveAccrualOccurrences",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAccrualOccurrences_TenantId_LeavePeriodId",
                table: "LeaveAccrualOccurrences",
                columns: new[] { "TenantId", "LeavePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAccrualOccurrences_TenantId_LeavePolicyRuleId",
                table: "LeaveAccrualOccurrences",
                columns: new[] { "TenantId", "LeavePolicyRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAccrualOccurrences_TenantId_LeavePolicyVersionId",
                table: "LeaveAccrualOccurrences",
                columns: new[] { "TenantId", "LeavePolicyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAccrualOccurrences_TenantId_LeaveTypeId",
                table: "LeaveAccrualOccurrences",
                columns: new[] { "TenantId", "LeaveTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAccrualOccurrences_TenantId_OccurrenceKey",
                table: "LeaveAccrualOccurrences",
                columns: new[] { "TenantId", "OccurrenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAccrualOccurrences_TenantId_Status_OccurrenceDate",
                table: "LeaveAccrualOccurrences",
                columns: new[] { "TenantId", "Status", "OccurrenceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveAccrualOccurrences");

            migrationBuilder.DropColumn(
                name: "CarryForwardEnabled",
                table: "LeavePolicyEntitlementRules");

            migrationBuilder.DropColumn(
                name: "CarryForwardExpiryDays",
                table: "LeavePolicyEntitlementRules");

            migrationBuilder.DropColumn(
                name: "MaximumAccumulation",
                table: "LeavePolicyEntitlementRules");

            migrationBuilder.DropColumn(
                name: "MaximumCarryForwardQuantity",
                table: "LeavePolicyEntitlementRules");

            migrationBuilder.DropColumn(
                name: "ProratePartialPeriod",
                table: "LeavePolicyEntitlementRules");
        }
    }
}
