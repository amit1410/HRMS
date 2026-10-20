using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWelcomeInvitationCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WelcomeInvitationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalRequested = table.Column<int>(type: "int", nullable: false),
                    TotalAccepted = table.Column<int>(type: "int", nullable: false),
                    TotalRejected = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelcomeInvitationBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WelcomeInvitationBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WelcomeInvitationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptType = table.Column<int>(type: "int", nullable: false),
                    DeliveryStatus = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecipientEmailSnapshot = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ErrorCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelcomeInvitationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WelcomeInvitationAttempts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WelcomeInvitationAttempts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WelcomeInvitationAttempts_Users_PortalUserId",
                        column: x => x.PortalUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WelcomeInvitationAttempts_WelcomeInvitationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "WelcomeInvitationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationAttempts_BatchId",
                table: "WelcomeInvitationAttempts",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationAttempts_EmployeeId",
                table: "WelcomeInvitationAttempts",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationAttempts_PortalUserId",
                table: "WelcomeInvitationAttempts",
                column: "PortalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationAttempts_TenantId_EmployeeId_RequestedAtUtc",
                table: "WelcomeInvitationAttempts",
                columns: new[] { "TenantId", "EmployeeId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationAttempts_TenantId_IdempotencyKey",
                table: "WelcomeInvitationAttempts",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationBatches_TenantId_RequestedAtUtc",
                table: "WelcomeInvitationBatches",
                columns: new[] { "TenantId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WelcomeInvitationAttempts");

            migrationBuilder.DropTable(
                name: "WelcomeInvitationBatches");
        }
    }
}
