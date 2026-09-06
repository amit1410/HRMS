using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlCatalogMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialMySqlCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "varchar(253)", maxLength: 253, nullable: false),
                    ShardKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DatabaseProvider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    TenantName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TenantBranding",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    IsPublic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    LogoUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    PrimaryColor = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: true),
                    WelcomeMessage = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: true),
                    SupportEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    SsoEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SsoProviderName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBranding", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantBranding_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Host",
                table: "Tenants",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ShardKey",
                table: "Tenants",
                column: "ShardKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantCode",
                table: "Tenants",
                column: "TenantCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantBranding");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
