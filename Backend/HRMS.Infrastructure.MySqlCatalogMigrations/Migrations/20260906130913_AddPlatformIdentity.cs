using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlCatalogMigrations.Migrations;

public partial class AddPlatformIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("PlatformPermissions", table => new
        {
            Id = table.Column<int>(type: "int", nullable: false),
            Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_PlatformPermissions", x => x.Id)).Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable("PlatformRoles", table => new
        {
            Id = table.Column<int>(type: "int", nullable: false),
            Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_PlatformRoles", x => x.Id)).Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable("PlatformUsers", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false),
            Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
            NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
            PasswordHash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
            FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
            LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
            IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
            SecurityRevision = table.Column<int>(type: "int", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
            LastLoginAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_PlatformUsers", x => x.Id)).Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable("PlatformRolePermissions", table => new
        {
            PlatformRoleId = table.Column<int>(type: "int", nullable: false),
            PlatformPermissionId = table.Column<int>(type: "int", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_PlatformRolePermissions", x => new { x.PlatformRoleId, x.PlatformPermissionId });
            table.ForeignKey("FK_PRP_Permission", x => x.PlatformPermissionId, "PlatformPermissions", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_PRP_Role", x => x.PlatformRoleId, "PlatformRoles", "Id", onDelete: ReferentialAction.Cascade);
        }).Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable("PlatformRefreshTokens", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false),
            PlatformUserId = table.Column<Guid>(type: "char(36)", nullable: false),
            TokenHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
            ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
            RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
            ReplacedByTokenId = table.Column<Guid>(type: "char(36)", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_PlatformRefreshTokens", x => x.Id);
            table.ForeignKey("FK_PRT_User", x => x.PlatformUserId, "PlatformUsers", "Id", onDelete: ReferentialAction.Cascade);
        }).Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable("PlatformUserRoles", table => new
        {
            PlatformUserId = table.Column<Guid>(type: "char(36)", nullable: false),
            PlatformRoleId = table.Column<int>(type: "int", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_PlatformUserRoles", x => new { x.PlatformUserId, x.PlatformRoleId });
            table.ForeignKey("FK_PUR_Role", x => x.PlatformRoleId, "PlatformRoles", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_PUR_User", x => x.PlatformUserId, "PlatformUsers", "Id", onDelete: ReferentialAction.Cascade);
        }).Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex("IX_PlatformPermissions_Name", "PlatformPermissions", "Name", unique: true);
        migrationBuilder.CreateIndex("IX_PlatformRoles_Name", "PlatformRoles", "Name", unique: true);
        migrationBuilder.CreateIndex("IX_PlatformUsers_NormalizedEmail", "PlatformUsers", "NormalizedEmail", unique: true);
        migrationBuilder.CreateIndex("IX_PlatformRefreshTokens_TokenHash", "PlatformRefreshTokens", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("IX_PlatformRefreshTokens_PlatformUserId_RevokedAtUtc", "PlatformRefreshTokens", new[] { "PlatformUserId", "RevokedAtUtc" });
        migrationBuilder.CreateIndex("IX_PlatformUserRoles_PlatformRoleId", "PlatformUserRoles", "PlatformRoleId");
        migrationBuilder.CreateIndex("IX_PlatformRolePermissions_PlatformPermissionId", "PlatformRolePermissions", "PlatformPermissionId");
        migrationBuilder.InsertData("PlatformRoles", new[] { "Id", "Name" }, new object[] { 1, "PlatformSuperAdmin" });
        migrationBuilder.InsertData("PlatformPermissions", new[] { "Id", "Name" }, new object[,] { { 1, "PlatformTenant.View" }, { 2, "PlatformTenant.Create" }, { 3, "PlatformTenant.UpdateStatus" } });
        migrationBuilder.InsertData("PlatformRolePermissions", new[] { "PlatformRoleId", "PlatformPermissionId" }, new object[,] { { 1, 1 }, { 1, 2 }, { 1, 3 } });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PlatformUserRoles");
        migrationBuilder.DropTable("PlatformRolePermissions");
        migrationBuilder.DropTable("PlatformRefreshTokens");
        migrationBuilder.DropTable("PlatformPermissions");
        migrationBuilder.DropTable("PlatformRoles");
        migrationBuilder.DropTable("PlatformUsers");
    }
}
