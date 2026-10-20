using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations;

public partial class AddWelcomeInvitationCenter : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("WelcomeInvitationBatches", t => new { Id = t.Column<Guid>(type: "char(36)"), TenantId = t.Column<Guid>(type: "char(36)"), RequestedByUserId = t.Column<Guid>(type: "char(36)"), RequestedAtUtc = t.Column<DateTime>(type: "datetime(6)"), TotalRequested = t.Column<int>(type: "int"), TotalAccepted = t.Column<int>(type: "int"), TotalRejected = t.Column<int>(type: "int"), Status = t.Column<int>(type: "int"), CorrelationId = t.Column<string>(type: "varchar(64)", maxLength: 64) }, constraints: t => { t.PrimaryKey("PK_WelcomeInvitationBatches", x => x.Id); t.ForeignKey("FK_WelcomeInvitationBatches_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade); }).Annotation("MySQL:Charset", "utf8mb4");
        m.CreateTable("WelcomeInvitationAttempts", t => new { Id = t.Column<Guid>(type: "char(36)"), TenantId = t.Column<Guid>(type: "char(36)"), EmployeeId = t.Column<Guid>(type: "char(36)"), PortalUserId = t.Column<Guid>(type: "char(36)", nullable: true), BatchId = t.Column<Guid>(type: "char(36)", nullable: true), AttemptType = t.Column<int>(type: "int"), DeliveryStatus = t.Column<int>(type: "int"), RequestedByUserId = t.Column<Guid>(type: "char(36)"), RequestedAtUtc = t.Column<DateTime>(type: "datetime(6)"), RecipientEmailSnapshot = t.Column<string>(type: "varchar(320)", maxLength: 320), ErrorCategory = t.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true), ErrorSummary = t.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true), CorrelationId = t.Column<string>(type: "varchar(64)", maxLength: 64), IdempotencyKey = t.Column<string>(type: "varchar(128)", maxLength: 128), SentAtUtc = t.Column<DateTime>(type: "datetime(6)", nullable: true), CompletedAtUtc = t.Column<DateTime>(type: "datetime(6)", nullable: true) }, constraints: t => { t.PrimaryKey("PK_WelcomeInvitationAttempts", x => x.Id); t.ForeignKey("FK_WelcomeInvitationAttempts_Employees_EmployeeId", x => x.EmployeeId, "Employees", "Id", onDelete: ReferentialAction.Restrict); t.ForeignKey("FK_WelcomeInvitationAttempts_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade); t.ForeignKey("FK_WelcomeInvitationAttempts_Users_PortalUserId", x => x.PortalUserId, "Users", "Id", onDelete: ReferentialAction.SetNull); t.ForeignKey("FK_WelcomeInvitationAttempts_WelcomeInvitationBatches_BatchId", x => x.BatchId, "WelcomeInvitationBatches", "Id", onDelete: ReferentialAction.SetNull); }).Annotation("MySQL:Charset", "utf8mb4");
        m.CreateIndex("IX_WelcomeInvitationAttempts_BatchId", "WelcomeInvitationAttempts", "BatchId");
        m.CreateIndex("IX_WelcomeInvitationAttempts_EmployeeId", "WelcomeInvitationAttempts", "EmployeeId");
        m.CreateIndex("IX_WelcomeInvitationAttempts_PortalUserId", "WelcomeInvitationAttempts", "PortalUserId");
        m.CreateIndex("IX_WelcomeInvitationAttempts_TenantId_EmployeeId_RequestedAtUtc", "WelcomeInvitationAttempts", new[] { "TenantId", "EmployeeId", "RequestedAtUtc" });
        m.CreateIndex("IX_WelcomeInvitationAttempts_TenantId_IdempotencyKey", "WelcomeInvitationAttempts", new[] { "TenantId", "IdempotencyKey" }, unique: true);
        m.CreateIndex("IX_WelcomeInvitationBatches_TenantId_RequestedAtUtc", "WelcomeInvitationBatches", new[] { "TenantId", "RequestedAtUtc" });
    }

    protected override void Down(MigrationBuilder m) { m.DropTable("WelcomeInvitationAttempts"); m.DropTable("WelcomeInvitationBatches"); }
}
