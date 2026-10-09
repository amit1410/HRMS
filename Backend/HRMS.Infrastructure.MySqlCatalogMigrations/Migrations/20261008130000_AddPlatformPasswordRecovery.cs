using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace HRMS.Infrastructure.MySqlCatalogMigrations.Migrations;

[DbContext(typeof(HrmsCatalogDbContext))]
[Migration("20261008130000_AddPlatformPasswordRecovery")]
public partial class AddPlatformPasswordRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "PlatformPasswordResetTokens", columns: table => new { Id = table.Column<Guid>(type: "char(36)", nullable: false), PlatformUserId = table.Column<Guid>(type: "char(36)", nullable: false), TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false), CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false), ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false), UsedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true) }, constraints: table => { table.PrimaryKey("PK_PlatformPasswordResetTokens", x => x.Id); table.ForeignKey("FK_PlatformPasswordResetTokens_PlatformUsers_PlatformUserId", x => x.PlatformUserId, "PlatformUsers", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex("IX_PlatformPasswordResetTokens_TokenHash", "PlatformPasswordResetTokens", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("IX_PlatformPasswordResetTokens_PlatformUserId_UsedAtUtc_ExpiresAtUtc", "PlatformPasswordResetTokens", new[] { "PlatformUserId", "UsedAtUtc", "ExpiresAtUtc" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("PlatformPasswordResetTokens");
}
