using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

public partial class AddWelcomeInvitationOutboxProcessingSql : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("CompletedAtUtc", "WelcomeInvitationBatches", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("IdempotencyKey", "WelcomeInvitationBatches", type: "nvarchar(128)", maxLength: 128, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<DateTime>("LastAttemptAtUtc", "WelcomeInvitationAttempts", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("LeaseExpiresAtUtc", "WelcomeInvitationAttempts", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("LeaseId", "WelcomeInvitationAttempts", type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<int>("MaxAttempts", "WelcomeInvitationAttempts", type: "int", nullable: false, defaultValue: 3);
        migrationBuilder.AddColumn<DateTime>("NextAttemptAtUtc", "WelcomeInvitationAttempts", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("ProcessingStartedAtUtc", "WelcomeInvitationAttempts", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<int>("RetryCount", "WelcomeInvitationAttempts", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.CreateIndex("IX_WelcomeInvitationBatches_TenantId_IdempotencyKey", "WelcomeInvitationBatches", new[] { "TenantId", "IdempotencyKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_WelcomeInvitationAttempts_Tenant_Status_Next_LeaseExp", "WelcomeInvitationAttempts", new[] { "TenantId", "DeliveryStatus", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_WelcomeInvitationBatches_TenantId_IdempotencyKey", "WelcomeInvitationBatches");
        migrationBuilder.DropIndex("IX_WelcomeInvitationAttempts_Tenant_Status_Next_LeaseExp", "WelcomeInvitationAttempts");
        foreach (var (column, table) in new[] { ("CompletedAtUtc", "WelcomeInvitationBatches"), ("IdempotencyKey", "WelcomeInvitationBatches"), ("LastAttemptAtUtc", "WelcomeInvitationAttempts"), ("LeaseExpiresAtUtc", "WelcomeInvitationAttempts"), ("LeaseId", "WelcomeInvitationAttempts"), ("MaxAttempts", "WelcomeInvitationAttempts"), ("NextAttemptAtUtc", "WelcomeInvitationAttempts"), ("ProcessingStartedAtUtc", "WelcomeInvitationAttempts"), ("RetryCount", "WelcomeInvitationAttempts") }) migrationBuilder.DropColumn(column, table);
    }
}
