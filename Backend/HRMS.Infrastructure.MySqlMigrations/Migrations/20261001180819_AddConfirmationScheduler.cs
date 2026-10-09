using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfirmationReminderDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationPolicyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConfirmationPolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProbationEndDate = table.Column<DateTime>(type: "date", nullable: false),
                    TriggerDate = table.Column<DateTime>(type: "date", nullable: false),
                    RecipientType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RecipientEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    RecipientEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    OccurrenceKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    NotificationType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "char(36)", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationReminderDeliveries", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationReminderDeliveries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationReminderDeliveries_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReminderDeliveries_Employees_TenantId_RecipientE~",
                        columns: x => new { x.TenantId, x.RecipientEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmationReminderDeliveries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConfirmationSchedulerSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FrequencyMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultTriggerDaysBeforeEnd = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: true),
                    ClaimLeaseMinutes = table.Column<int>(type: "int", nullable: true),
                    ResendProcessed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NotifyEmployee = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NotifyManager = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NotifyHrbp = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmationSchedulerSettings", x => x.Id);
                    table.UniqueConstraint("AK_ConfirmationSchedulerSettings_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfirmationSchedulerSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReminderDeliveries_TenantId_EmployeeId_Probation~",
                table: "ConfirmationReminderDeliveries",
                columns: new[] { "TenantId", "EmployeeId", "ProbationEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReminderDeliveries_TenantId_OccurrenceKey",
                table: "ConfirmationReminderDeliveries",
                columns: new[] { "TenantId", "OccurrenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReminderDeliveries_TenantId_RecipientEmployeeId",
                table: "ConfirmationReminderDeliveries",
                columns: new[] { "TenantId", "RecipientEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationReminderDeliveries_TenantId_Status",
                table: "ConfirmationReminderDeliveries",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationSchedulerSettings_TenantId",
                table: "ConfirmationSchedulerSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfirmationReminderDeliveries");

            migrationBuilder.DropTable(
                name: "ConfirmationSchedulerSettings");
        }
    }
}
