using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                }, constraints: table => table.PrimaryKey("PK_PlatformPermissions", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PlatformRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                }, constraints: table => table.PrimaryKey("PK_PlatformRoles", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PlatformUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SecurityRevision = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                }, constraints: table => table.PrimaryKey("PK_PlatformUsers", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PlatformRolePermissions",
                columns: table => new
                {
                    PlatformRoleId = table.Column<int>(type: "int", nullable: false),
                    PlatformPermissionId = table.Column<int>(type: "int", nullable: false)
                }, constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRolePermissions", x => new { x.PlatformRoleId, x.PlatformPermissionId });
                    table.ForeignKey("FK_PlatformRolePermissions_PlatformPermissions_PlatformPermissionId", x => x.PlatformPermissionId, "PlatformPermissions", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_PlatformRolePermissions_PlatformRoles_PlatformRoleId", x => x.PlatformRoleId, "PlatformRoles", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                }, constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRefreshTokens", x => x.Id);
                    table.ForeignKey("FK_PlatformRefreshTokens_PlatformUsers_PlatformUserId", x => x.PlatformUserId, "PlatformUsers", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUserRoles",
                columns: table => new
                {
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformRoleId = table.Column<int>(type: "int", nullable: false)
                }, constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUserRoles", x => new { x.PlatformUserId, x.PlatformRoleId });
                    table.ForeignKey("FK_PlatformUserRoles_PlatformRoles_PlatformRoleId", x => x.PlatformRoleId, "PlatformRoles", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_PlatformUserRoles_PlatformUsers_PlatformUserId", x => x.PlatformUserId, "PlatformUsers", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_PlatformPermissions_Name", "PlatformPermissions", "Name", unique: true);
            migrationBuilder.CreateIndex("IX_PlatformRoles_Name", "PlatformRoles", "Name", unique: true);
            migrationBuilder.CreateIndex("IX_PlatformUsers_NormalizedEmail", "PlatformUsers", "NormalizedEmail", unique: true);
            migrationBuilder.CreateIndex("IX_PlatformRefreshTokens_TokenHash", "PlatformRefreshTokens", "TokenHash", unique: true);
            migrationBuilder.CreateIndex("IX_PlatformRefreshTokens_PlatformUserId_RevokedAtUtc", "PlatformRefreshTokens", new[] { "PlatformUserId", "RevokedAtUtc" });

            migrationBuilder.InsertData("PlatformRoles", new[] { "Id", "Name" }, new object[] { 1, "PlatformSuperAdmin" });
            migrationBuilder.InsertData("PlatformPermissions", new[] { "Id", "Name" }, new object[,]
            {
                { 1, "PlatformTenant.View" },
                { 2, "PlatformTenant.Create" },
                { 3, "PlatformTenant.UpdateStatus" }
            });
            migrationBuilder.InsertData("PlatformRolePermissions", new[] { "PlatformRoleId", "PlatformPermissionId" }, new object[,]
            {
                { 1, 1 }, { 1, 2 }, { 1, 3 }
            });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlatformUserRoles");
            migrationBuilder.DropTable(name: "PlatformRolePermissions");
            migrationBuilder.DropTable(name: "PlatformRefreshTokens");
            migrationBuilder.DropTable(name: "PlatformPermissions");
            migrationBuilder.DropTable(name: "PlatformRoles");
            migrationBuilder.DropTable(name: "PlatformUsers");
        }
    }
}
