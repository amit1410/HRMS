using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlCatalogMigrations.Migrations;

public partial class AddPasswordRecoverySettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("PasswordRecoveryEnabled", "TenantBranding", type: "tinyint(1)", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("AllowEmailOtp", "TenantBranding", type: "tinyint(1)", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("AllowSmsOtp", "TenantBranding", type: "tinyint(1)", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<int>("OtpExpiryMinutes", "TenantBranding", type: "int", nullable: false, defaultValue: 5);
        migrationBuilder.AddColumn<int>("OtpMaxAttempts", "TenantBranding", type: "int", nullable: false, defaultValue: 5);
        migrationBuilder.AddColumn<int>("OtpResendCooldownSeconds", "TenantBranding", type: "int", nullable: false, defaultValue: 60);
        migrationBuilder.AddColumn<int>("OtpMaxResends", "TenantBranding", type: "int", nullable: false, defaultValue: 5);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("PasswordRecoveryEnabled", "TenantBranding");
        migrationBuilder.DropColumn("AllowEmailOtp", "TenantBranding");
        migrationBuilder.DropColumn("AllowSmsOtp", "TenantBranding");
        migrationBuilder.DropColumn("OtpExpiryMinutes", "TenantBranding");
        migrationBuilder.DropColumn("OtpMaxAttempts", "TenantBranding");
        migrationBuilder.DropColumn("OtpResendCooldownSeconds", "TenantBranding");
        migrationBuilder.DropColumn("OtpMaxResends", "TenantBranding");
    }
}
