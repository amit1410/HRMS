using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Catalog.Migrations
{
    public partial class AddPasswordRecoverySettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "PasswordRecoveryEnabled", table: "TenantBranding", type: "bit", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "AllowEmailOtp", table: "TenantBranding", type: "bit", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "AllowSmsOtp", table: "TenantBranding", type: "bit", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<int>(name: "OtpExpiryMinutes", table: "TenantBranding", type: "int", nullable: false, defaultValue: 5);
            migrationBuilder.AddColumn<int>(name: "OtpMaxAttempts", table: "TenantBranding", type: "int", nullable: false, defaultValue: 5);
            migrationBuilder.AddColumn<int>(name: "OtpResendCooldownSeconds", table: "TenantBranding", type: "int", nullable: false, defaultValue: 60);
            migrationBuilder.AddColumn<int>(name: "OtpMaxResends", table: "TenantBranding", type: "int", nullable: false, defaultValue: 5);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PasswordRecoveryEnabled", table: "TenantBranding");
            migrationBuilder.DropColumn(name: "AllowEmailOtp", table: "TenantBranding");
            migrationBuilder.DropColumn(name: "AllowSmsOtp", table: "TenantBranding");
            migrationBuilder.DropColumn(name: "OtpExpiryMinutes", table: "TenantBranding");
            migrationBuilder.DropColumn(name: "OtpMaxAttempts", table: "TenantBranding");
            migrationBuilder.DropColumn(name: "OtpResendCooldownSeconds", table: "TenantBranding");
            migrationBuilder.DropColumn(name: "OtpMaxResends", table: "TenantBranding");
        }
    }
}
