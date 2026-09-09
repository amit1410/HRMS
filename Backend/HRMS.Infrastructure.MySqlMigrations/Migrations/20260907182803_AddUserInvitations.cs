using Microsoft.EntityFrameworkCore.Migrations;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20260907182803_AddUserInvitations")]
public partial class AddUserInvitations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("UserInvitations", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false), TenantId = table.Column<Guid>(type: "char(36)", nullable: false), UserId = table.Column<Guid>(type: "char(36)", nullable: false), TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false), Purpose = table.Column<int>(type: "int", nullable: false), CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false), ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false), UsedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true), RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true), CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false), LastSentAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_UserInvitations", x => x.Id); table.ForeignKey("FK_UserInvitations_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_UserInvitations_Users_TenantId_UserId", x => new { x.TenantId, x.UserId }, "Users", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Cascade); }).Annotation("MySQL:Charset", "utf8mb4");
        migrationBuilder.CreateIndex("IX_UserInvitations_UserPurposeStatus", "UserInvitations", new[] { "TenantId", "UserId", "Purpose", "UsedAtUtc", "RevokedAtUtc" });
        migrationBuilder.CreateIndex("IX_UserInvitations_TokenHash", "UserInvitations", "TokenHash", unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("UserInvitations");
}
