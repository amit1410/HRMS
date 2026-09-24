using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
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
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeSeparationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollFinalSettlementId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReadinessCheckedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    InitiatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    InitiatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastFailureCode = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ApprovedLastWorkingDateSnapshot = table.Column<DateTime>(type: "date", nullable: true),
                    NoticeStartDateSnapshot = table.Column<DateTime>(type: "date", nullable: true),
                    RequiredNoticeDaysSnapshot = table.Column<int>(type: "int", nullable: true),
                    ServedNoticeDaysSnapshot = table.Column<int>(type: "int", nullable: true),
                    WaivedNoticeDaysSnapshot = table.Column<int>(type: "int", nullable: false),
                    NoticeShortfallDaysSnapshot = table.Column<int>(type: "int", nullable: true),
                    ClearanceIdSnapshot = table.Column<Guid>(type: "char(36)", nullable: true),
                    ClearanceCompletedAtSnapshotUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PendingAssetRecoveryCountSnapshot = table.Column<int>(type: "int", nullable: false),
                    ExitInterviewIdSnapshot = table.Column<Guid>(type: "char(36)", nullable: true),
                    ExitInterviewDispositionSnapshot = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationSettlementOrchestrations", x => x.Id);
                    table.UniqueConstraint("AK_SeparationSettlementOrchestrations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationSettlementOrchestrations_EmployeeSeparations_Tenan~",
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SeparationSettlementEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SeparationSettlementOrchestrationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    MetadataJson = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationSettlementEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationSettlementEvents_SeparationSettlementOrchestration~",
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SeparationSettlementEvents_TenantId_SeparationSettlementOrch~",
                table: "SeparationSettlementEvents",
                columns: new[] { "TenantId", "SeparationSettlementOrchestrationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationSettlementOrchestrations_TenantId_EmployeeSeparati~",
                table: "SeparationSettlementOrchestrations",
                columns: new[] { "TenantId", "EmployeeSeparationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationSettlementOrchestrations_TenantId_PayrollFinalSett~",
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
