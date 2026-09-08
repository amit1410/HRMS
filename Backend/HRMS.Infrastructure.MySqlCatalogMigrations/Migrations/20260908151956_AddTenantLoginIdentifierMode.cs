using Microsoft.EntityFrameworkCore.Migrations;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace HRMS.Infrastructure.MySqlCatalogMigrations.Migrations;

[DbContext(typeof(HrmsCatalogDbContext))]
[Migration("20260908151956_AddTenantLoginIdentifierMode")]
public partial class AddTenantLoginIdentifierMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<int>("LoginIdentifierMode", "TenantBranding", type: "int", nullable: false, defaultValue: 2);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn("LoginIdentifierMode", "TenantBranding");
}
