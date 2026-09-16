using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20260915100000_EnhanceUserRoleAssignments")]
public partial class EnhanceUserRoleAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("Id", "UserRoles", type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()");
        migrationBuilder.AddColumn<DateOnly>("EffectiveFrom", "UserRoles", type: "date", nullable: false, defaultValue: new DateOnly(2026, 9, 15));
        migrationBuilder.AddColumn<DateOnly>("EffectiveTo", "UserRoles", type: "date", nullable: true);
        migrationBuilder.AddColumn<int>("AssignmentSource", "UserRoles", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<Guid>("AssignedByUserId", "UserRoles", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>("AssignmentReason", "UserRoles", type: "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<DateTime>("CreatedAtUtc", "UserRoles", type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()");
        migrationBuilder.AddColumn<DateTime>("UpdatedAtUtc", "UserRoles", type: "datetime2", nullable: true);
        migrationBuilder.DropPrimaryKey("PK_UserRoles", "UserRoles");
        migrationBuilder.AddPrimaryKey("PK_UserRoles", "UserRoles", "Id");
        migrationBuilder.CreateIndex("IX_UserRoles_TenantId_UserId_RoleId_EffectiveFrom", "UserRoles", new[] { "TenantId", "UserId", "RoleId", "EffectiveFrom" });

        migrationBuilder.CreateTable("UserRoleAssignmentEvents", table => new
        {
            Id = table.Column<Guid>("uniqueidentifier", nullable: false),
            TenantId = table.Column<Guid>("uniqueidentifier", nullable: false),
            AssignmentId = table.Column<Guid>("uniqueidentifier", nullable: false),
            UserId = table.Column<Guid>("uniqueidentifier", nullable: false),
            RoleId = table.Column<int>("int", nullable: false),
            EventType = table.Column<int>("int", nullable: false),
            EffectiveFrom = table.Column<DateOnly>("date", nullable: false),
            EffectiveTo = table.Column<DateOnly>("date", nullable: true),
            AssignmentSource = table.Column<int>("int", nullable: false),
            Reason = table.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            PerformedByUserId = table.Column<Guid>("uniqueidentifier", nullable: true),
            OccurredAtUtc = table.Column<DateTime>("datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_UserRoleAssignmentEvents", x => x.Id);
            table.ForeignKey("FK_UserRoleAssignmentEvents_UserRoles_AssignmentId", x => x.AssignmentId, "UserRoles", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_URAEvent_Tenant_Assignment_Occurred_Id", "UserRoleAssignmentEvents", new[] { "TenantId", "AssignmentId", "OccurredAtUtc", "Id" });

        migrationBuilder.CreateTable("UserRoleAssignmentScopes", table => new
        {
            Id = table.Column<Guid>("uniqueidentifier", nullable: false),
            TenantId = table.Column<Guid>("uniqueidentifier", nullable: false),
            UserRoleAssignmentId = table.Column<Guid>("uniqueidentifier", nullable: false),
            ScopeType = table.Column<int>("int", nullable: false),
            ScopeEntityId = table.Column<Guid>("uniqueidentifier", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_UserRoleAssignmentScopes", x => x.Id);
            table.ForeignKey("FK_UserRoleAssignmentScopes_UserRoles_UserRoleAssignmentId", x => x.UserRoleAssignmentId, "UserRoles", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_URAScope_Assignment_Type_Entity", "UserRoleAssignmentScopes", new[] { "TenantId", "UserRoleAssignmentId", "ScopeType", "ScopeEntityId" }, unique: true);

        migrationBuilder.Sql("UPDATE ur SET AssignmentSource = CASE WHEN r.Name IN ('Employee','Manager') THEN 0 ELSE 1 END, AssignmentReason = CASE WHEN AssignmentReason IS NULL THEN 'Legacy role assignment backfill' ELSE AssignmentReason END FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId;");
        migrationBuilder.Sql("INSERT INTO UserRoleAssignmentEvents (Id,TenantId,AssignmentId,UserId,RoleId,EventType,EffectiveFrom,EffectiveTo,AssignmentSource,Reason,PerformedByUserId,OccurredAtUtc) SELECT NEWID(),ur.TenantId,ur.Id,ur.UserId,ur.RoleId,0,ur.EffectiveFrom,ur.EffectiveTo,ur.AssignmentSource, 'Legacy role assignment backfill', NULL, CAST('2026-09-15T00:00:00' AS datetime2) FROM UserRoles ur WHERE NOT EXISTS (SELECT 1 FROM UserRoleAssignmentEvents e WHERE e.AssignmentId = ur.Id);");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("UserRole assignment enrichment is irreversible because it changes the primary key and adds audit history.");
}
