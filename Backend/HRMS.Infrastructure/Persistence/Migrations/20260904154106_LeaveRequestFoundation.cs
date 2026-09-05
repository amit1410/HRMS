using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaveRequestFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EmployeeEmploymentHistory_TenantId_EmployeeId_Id",
                table: "EmployeeEmploymentHistory",
                columns: new[] { "TenantId", "EmployeeId", "Id" });

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeEmploymentHistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyGenderSnapshot = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ChargeableQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.UniqueConstraint("AK_LeaveRequests_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LeaveRequests_DateAndQuantity", "[StartDate] <= [EndDate] AND [RequestedQuantity] >= 0 AND [ChargeableQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_LeaveRequests_EmployeeEmploymentHistory_TenantId_EmployeeId_EmployeeEmploymentHistoryId",
                        columns: x => new { x.TenantId, x.EmployeeId, x.EmployeeEmploymentHistoryId },
                        principalTable: "EmployeeEmploymentHistory",
                        principalColumns: new[] { "TenantId", "EmployeeId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeavePeriods_TenantId_LeavePeriodId",
                        columns: x => new { x.TenantId, x.LeavePeriodId },
                        principalTable: "LeavePeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeavePolicyRules_TenantId_LeavePolicyVersionId_LeavePolicyRuleId",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId, x.LeavePolicyRuleId },
                        principalTable: "LeavePolicyRules",
                        principalColumns: new[] { "TenantId", "LeavePolicyVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeavePolicyVersions_TenantId_LeavePolicyVersionId",
                        columns: x => new { x.TenantId, x.LeavePolicyVersionId },
                        principalTable: "LeavePolicyVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_TenantId_LeaveTypeId",
                        columns: x => new { x.TenantId, x.LeaveTypeId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequestDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ChargeableQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    DayClassification = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CalculationReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsEmployeeRequested = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestDays", x => x.Id);
                    table.UniqueConstraint("AK_LeaveRequestDays_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LeaveRequestDays_NonNegativeQuantity", "[RequestedQuantity] >= 0 AND [ChargeableQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_LeaveRequestDays_LeaveRequests_TenantId_LeaveRequestId",
                        columns: x => new { x.TenantId, x.LeaveRequestId },
                        principalTable: "LeaveRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestDays_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequestEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestEvents", x => x.Id);
                    table.UniqueConstraint("AK_LeaveRequestEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_Employees_TenantId_ActorEmployeeId",
                        columns: x => new { x.TenantId, x.ActorEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_LeaveRequests_TenantId_LeaveRequestId",
                        columns: x => new { x.TenantId, x.LeaveRequestId },
                        principalTable: "LeaveRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequestEvents_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestDays_TenantId_LeaveRequestId_Date",
                table: "LeaveRequestDays",
                columns: new[] { "TenantId", "LeaveRequestId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestEvents_TenantId_ActorEmployeeId",
                table: "LeaveRequestEvents",
                columns: new[] { "TenantId", "ActorEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestEvents_TenantId_ActorUserId",
                table: "LeaveRequestEvents",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestEvents_TenantId_LeaveRequestId_OccurredAtUtc_Id",
                table: "LeaveRequestEvents",
                columns: new[] { "TenantId", "LeaveRequestId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_EmployeeId_EmployeeEmploymentHistoryId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "EmployeeId", "EmployeeEmploymentHistoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_EmployeeId_IdempotencyKey",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "EmployeeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_EmployeeId_StartDate_EndDate_Status",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "EmployeeId", "StartDate", "EndDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_LeavePeriodId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "LeavePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_LeavePolicyVersionId_LeavePolicyRuleId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "LeavePolicyVersionId", "LeavePolicyRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_TenantId_LeaveTypeId",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "LeaveTypeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveRequestDays");

            migrationBuilder.DropTable(
                name: "LeaveRequestEvents");

            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EmployeeEmploymentHistory_TenantId_EmployeeId_Id",
                table: "EmployeeEmploymentHistory");
        }
    }
}
