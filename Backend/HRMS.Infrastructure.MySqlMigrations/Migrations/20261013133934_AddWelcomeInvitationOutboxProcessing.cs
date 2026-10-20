using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddWelcomeInvitationOutboxProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "WelcomeInvitationBatches",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "WelcomeInvitationBatches",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAtUtc",
                table: "WelcomeInvitationAttempts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "WelcomeInvitationAttempts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseId",
                table: "WelcomeInvitationAttempts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "WelcomeInvitationAttempts",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "WelcomeInvitationAttempts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAtUtc",
                table: "WelcomeInvitationAttempts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "WelcomeInvitationAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationBatches_TenantId_IdempotencyKey",
                table: "WelcomeInvitationBatches",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeInvitationAttempts_Tenant_Status_Next_LeaseExp",
                table: "WelcomeInvitationAttempts",
                columns: new[] { "TenantId", "DeliveryStatus", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WelcomeInvitationBatches_TenantId_IdempotencyKey",
                table: "WelcomeInvitationBatches");

            migrationBuilder.DropIndex(
                name: "IX_WelcomeInvitationAttempts_Tenant_Status_Next_LeaseExp",
                table: "WelcomeInvitationAttempts");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "WelcomeInvitationBatches");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "WelcomeInvitationBatches");

            migrationBuilder.DropColumn(
                name: "LastAttemptAtUtc",
                table: "WelcomeInvitationAttempts");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "WelcomeInvitationAttempts");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "WelcomeInvitationAttempts");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "WelcomeInvitationAttempts");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "WelcomeInvitationAttempts");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAtUtc",
                table: "WelcomeInvitationAttempts");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "WelcomeInvitationAttempts");
        }
    }
}
