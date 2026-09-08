using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations;

public partial class AddPasswordResetOtps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("PasswordResetOtps", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false), TenantId = table.Column<Guid>(type: "char(36)", nullable: false), UserId = table.Column<Guid>(type: "char(36)", nullable: false),
            Channel = table.Column<int>(type: "int", nullable: false), Purpose = table.Column<int>(type: "int", nullable: false), ChallengeIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false), OtpHash = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false), DestinationHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true), MaskedDestination = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false), ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false), VerifiedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true), ConsumedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true), RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true), AttemptCount = table.Column<int>(type: "int", nullable: false), ResendCount = table.Column<int>(type: "int", nullable: false), LastSentAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false), CorrelationId = table.Column<Guid>(type: "char(36)", nullable: true), CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false), ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_PasswordResetOtps", x => x.Id); table.ForeignKey("FK_PasswordResetOtps_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_PasswordResetOtps_Users_TenantId_UserId", x => new { x.TenantId, x.UserId }, "Users", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Cascade); }).Annotation("MySQL:Charset", "utf8mb4");
        migrationBuilder.CreateIndex("IX_PasswordResetOtps_TenantId_ChallengeIdHash_Channel", "PasswordResetOtps", new[] { "TenantId", "ChallengeIdHash", "Channel" }, unique: true);
        migrationBuilder.CreateIndex("IX_PasswordResetOtps_TenantId_UserId_RevokedAtUtc_ConsumedAtUtc", "PasswordResetOtps", new[] { "TenantId", "UserId", "RevokedAtUtc", "ConsumedAtUtc" });
        migrationBuilder.CreateIndex("IX_PasswordResetOtps_TenantId_ExpiresAtUtc", "PasswordResetOtps", new[] { "TenantId", "ExpiresAtUtc" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("PasswordResetOtps");
}
