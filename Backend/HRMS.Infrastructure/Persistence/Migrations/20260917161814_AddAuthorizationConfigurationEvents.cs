using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationConfigurationEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorizationConfigurationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoleId = table.Column<int>(type: "int", nullable: true),
                    UserRoleAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PermissionId = table.Column<int>(type: "int", nullable: true),
                    PermissionCode = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ScopeDimension = table.Column<int>(type: "int", nullable: true),
                    ScopeValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScopeValueDisplay = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationConfigurationEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthConfigEvent_Tenant_Actor_Occurred",
                table: "AuthorizationConfigurationEvents",
                columns: new[] { "TenantId", "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthConfigEvent_Tenant_Assignment_Occurred",
                table: "AuthorizationConfigurationEvents",
                columns: new[] { "TenantId", "UserRoleAssignmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthConfigEvent_Tenant_Occurred_Id",
                table: "AuthorizationConfigurationEvents",
                columns: new[] { "TenantId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthConfigEvent_Tenant_Role_Occurred",
                table: "AuthorizationConfigurationEvents",
                columns: new[] { "TenantId", "RoleId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorizationConfigurationEvents");

        }
    }
}
