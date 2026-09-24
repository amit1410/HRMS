using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationSettlementOrchestrationPhase8F : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeparationSettlementOrchestrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeSeparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollFinalSettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReadinessCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InitiatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InitiatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFailureCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedLastWorkingDateSnapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticeStartDateSnapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    RequiredNoticeDaysSnapshot = table.Column<int>(type: "int", nullable: true),
                    ServedNoticeDaysSnapshot = table.Column<int>(type: "int", nullable: true),
                    WaivedNoticeDaysSnapshot = table.Column<int>(type: "int", nullable: false),
                    NoticeShortfallDaysSnapshot = table.Column<int>(type: "int", nullable: true),
                    ClearanceIdSnapshot = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClearanceCompletedAtSnapshotUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PendingAssetRecoveryCountSnapshot = table.Column<int>(type: "int", nullable: false),
                    ExitInterviewIdSnapshot = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExitInterviewDispositionSnapshot = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationSettlementOrchestrations", x => x.Id);
                    table.UniqueConstraint("AK_SeparationSettlementOrchestrations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationSettlementOrchestrations_EmployeeSeparations_TenantId_EmployeeSeparationId",
                        columns: x => new { x.TenantId, x.EmployeeSeparationId },
                        principalTable: "EmployeeSeparations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationSettlementOrchestrations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationSettlementEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeparationSettlementOrchestrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationSettlementEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationSettlementEvents_SeparationSettlementOrchestrations_TenantId_SeparationSettlementOrchestrationId",
                        columns: x => new { x.TenantId, x.SeparationSettlementOrchestrationId },
                        principalTable: "SeparationSettlementOrchestrations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationSettlementEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationSettlementEvents_TenantId_SeparationSettlementOrchestrationId_OccurredAtUtc",
                table: "SeparationSettlementEvents",
                columns: new[] { "TenantId", "SeparationSettlementOrchestrationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationSettlementOrchestrations_TenantId_EmployeeSeparationId",
                table: "SeparationSettlementOrchestrations",
                columns: new[] { "TenantId", "EmployeeSeparationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationSettlementOrchestrations_TenantId_PayrollFinalSettlementId",
                table: "SeparationSettlementOrchestrations",
                columns: new[] { "TenantId", "PayrollFinalSettlementId" },
                unique: true,
                filter: "[PayrollFinalSettlementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SeparationSettlementOrchestrations_TenantId_Status",
                table: "SeparationSettlementOrchestrations",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeparationSettlementEvents");

            migrationBuilder.DropTable(
                name: "SeparationSettlementOrchestrations");
        }
    }
}
