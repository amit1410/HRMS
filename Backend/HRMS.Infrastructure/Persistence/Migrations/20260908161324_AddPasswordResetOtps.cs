using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    public partial class AddPasswordResetOtps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "PasswordResetOtps", columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Channel = table.Column<int>(type: "int", nullable: false),
                Purpose = table.Column<int>(type: "int", nullable: false),
                ChallengeIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OtpHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                DestinationHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                MaskedDestination = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                ResendCount = table.Column<int>(type: "int", nullable: false),
                LastSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_PasswordResetOtps", x => x.Id);
                table.ForeignKey(name: "FK_PasswordResetOtps_Tenants_TenantId", column: x => x.TenantId, principalTable: "Tenants", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_PasswordResetOtps_Users_TenantId_UserId", columns: x => new { x.TenantId, x.UserId }, principalTable: "Users", principalColumns: new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_PasswordResetOtps_TenantId_ChallengeIdHash_Channel", table: "PasswordResetOtps", columns: new[] { "TenantId", "ChallengeIdHash", "Channel" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_PasswordResetOtps_TenantId_UserId_RevokedAtUtc_ConsumedAtUtc", table: "PasswordResetOtps", columns: new[] { "TenantId", "UserId", "RevokedAtUtc", "ConsumedAtUtc" });
            migrationBuilder.CreateIndex(name: "IX_PasswordResetOtps_TenantId_ExpiresAtUtc", table: "PasswordResetOtps", columns: new[] { "TenantId", "ExpiresAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "PasswordResetOtps");
    }
}
