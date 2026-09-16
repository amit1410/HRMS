using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20260915100000_EnhanceUserRoleAssignments")]
public partial class EnhanceUserRoleAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("Id", "UserRoles", type: "char(36)", nullable: true);
        migrationBuilder.Sql("UPDATE UserRoles SET Id = UUID() WHERE Id IS NULL;");
        migrationBuilder.AlterColumn<Guid>("Id", "UserRoles", type: "char(36)", nullable: false, oldClrType: typeof(Guid), oldType: "char(36)", oldNullable: true);
        migrationBuilder.AddColumn<DateOnly>("EffectiveFrom", "UserRoles", type: "date", nullable: false, defaultValueSql: "'2026-09-15'");
        migrationBuilder.AddColumn<DateOnly>("EffectiveTo", "UserRoles", type: "date", nullable: true);
        migrationBuilder.AddColumn<int>("AssignmentSource", "UserRoles", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<Guid>("AssignedByUserId", "UserRoles", type: "char(36)", nullable: true);
        migrationBuilder.AddColumn<string>("AssignmentReason", "UserRoles", type: "varchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<DateTime>("CreatedAtUtc", "UserRoles", type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)");
        migrationBuilder.AddColumn<DateTime>("UpdatedAtUtc", "UserRoles", type: "datetime(6)", nullable: true);
        migrationBuilder.CreateIndex("IX_UserRoles_UserId", "UserRoles", "UserId");
        migrationBuilder.Sql("ALTER TABLE `UserRoles` DROP PRIMARY KEY, ADD PRIMARY KEY (`Id`);");
        migrationBuilder.CreateIndex("IX_UserRoles_Id", "UserRoles", "Id", unique: true);
        migrationBuilder.CreateIndex("IX_UserRoles_TenantId_UserId_RoleId_EffectiveFrom", "UserRoles", new[] { "TenantId", "UserId", "RoleId", "EffectiveFrom" });
        migrationBuilder.CreateTable("UserRoleAssignmentEvents", table => new
        {
            Id = table.Column<Guid>("char(36)", nullable: false), TenantId = table.Column<Guid>("char(36)", nullable: false), AssignmentId = table.Column<Guid>("char(36)", nullable: false), UserId = table.Column<Guid>("char(36)", nullable: false), RoleId = table.Column<int>("int", nullable: false), EventType = table.Column<int>("int", nullable: false), EffectiveFrom = table.Column<DateOnly>("date", nullable: false), EffectiveTo = table.Column<DateOnly>("date", nullable: true), AssignmentSource = table.Column<int>("int", nullable: false), Reason = table.Column<string>("varchar(500)", maxLength: 500, nullable: true), PerformedByUserId = table.Column<Guid>("char(36)", nullable: true), OccurredAtUtc = table.Column<DateTime>("datetime(6)", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_UserRoleAssignmentEvents", x => x.Id); table.ForeignKey("FK_UserRoleAssignmentEvents_UserRoles_AssignmentId", x => x.AssignmentId, "UserRoles", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("IX_URAEvent_Tenant_Assignment_Occurred_Id", "UserRoleAssignmentEvents", new[] { "TenantId", "AssignmentId", "OccurredAtUtc", "Id" });
        migrationBuilder.CreateTable("UserRoleAssignmentScopes", table => new
        {
            Id = table.Column<Guid>("char(36)", nullable: false), TenantId = table.Column<Guid>("char(36)", nullable: false), UserRoleAssignmentId = table.Column<Guid>("char(36)", nullable: false), ScopeType = table.Column<int>("int", nullable: false), ScopeEntityId = table.Column<Guid>("char(36)", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_UserRoleAssignmentScopes", x => x.Id); table.ForeignKey("FK_UserRoleAssignmentScopes_UserRoles_UserRoleAssignmentId", x => x.UserRoleAssignmentId, "UserRoles", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("UX_URAScope_Assignment_Type_Entity", "UserRoleAssignmentScopes", new[] { "TenantId", "UserRoleAssignmentId", "ScopeType", "ScopeEntityId" }, unique: true);
        migrationBuilder.Sql("UPDATE UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId SET ur.AssignmentSource = IF(r.Name IN ('Employee','Manager'),0,1), ur.AssignmentReason = IFNULL(ur.AssignmentReason,'Legacy role assignment backfill');");
        migrationBuilder.Sql("INSERT INTO UserRoleAssignmentEvents (Id,TenantId,AssignmentId,UserId,RoleId,EventType,EffectiveFrom,EffectiveTo,AssignmentSource,Reason,PerformedByUserId,OccurredAtUtc) SELECT UUID(),ur.TenantId,ur.Id,ur.UserId,ur.RoleId,0,ur.EffectiveFrom,ur.EffectiveTo,ur.AssignmentSource,'Legacy role assignment backfill',NULL,'2026-09-15 00:00:00.000000' FROM UserRoles ur LEFT JOIN UserRoleAssignmentEvents e ON e.AssignmentId=ur.Id WHERE e.Id IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("UserRole assignment enrichment is irreversible because it changes the primary key and adds audit history.");
}
