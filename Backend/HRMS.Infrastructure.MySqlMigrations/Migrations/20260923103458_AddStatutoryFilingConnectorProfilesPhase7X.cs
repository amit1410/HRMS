using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStatutoryFilingConnectorProfilesPhase7X : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResubmissionOfSubmissionId",
                table: "StatutoryFilingSubmissions",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousPackageId",
                table: "StatutoryFilingPackages",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResubmissionOfSubmissionId",
                table: "StatutoryFilingPackages",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionProfileId",
                table: "StatutoryFilingDefinitions",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StatutoryFilingConnectionProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    ConnectorType = table.Column<int>(type: "int", nullable: false),
                    Endpoint = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    NonSecretConfigurationJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    SecretReference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastValidationStatus = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryFilingConnectionProfiles", x => x.Id);
                    table.UniqueConstraint("AK_StatutoryFilingConnectionProfiles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StatutoryFilingConnectionProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmission~",
                table: "StatutoryFilingSubmissions",
                columns: new[] { "TenantId", "ResubmissionOfSubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingPackages_TenantId_PreviousPackageId",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "PreviousPackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingPackages_TenantId_ResubmissionOfSubmissionId",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "ResubmissionOfSubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingDefinitions_TenantId_ConnectionProfileId",
                table: "StatutoryFilingDefinitions",
                columns: new[] { "TenantId", "ConnectionProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingConnectionProfiles_TenantId_Name",
                table: "StatutoryFilingConnectionProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StatutoryFilingDefinitions_StatutoryFilingConnectionProfiles~",
                table: "StatutoryFilingDefinitions",
                columns: new[] { "TenantId", "ConnectionProfileId" },
                principalTable: "StatutoryFilingConnectionProfiles",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingPackages_TenantId_Pre~",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "PreviousPackageId" },
                principalTable: "StatutoryFilingPackages",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingSubmissions_TenantId_~",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "ResubmissionOfSubmissionId" },
                principalTable: "StatutoryFilingSubmissions",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_StatutoryFilingSubmissions_StatutoryFilingSubmissions_Tenant~",
                table: "StatutoryFilingSubmissions",
                columns: new[] { "TenantId", "ResubmissionOfSubmissionId" },
                principalTable: "StatutoryFilingSubmissions",
                principalColumns: new[] { "TenantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingDefinitions_StatutoryFilingConnectionProfiles~",
                table: "StatutoryFilingDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingPackages_TenantId_Pre~",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingSubmissions_TenantId_~",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingSubmissions_StatutoryFilingSubmissions_Tenant~",
                table: "StatutoryFilingSubmissions");

            migrationBuilder.DropTable(
                name: "StatutoryFilingConnectionProfiles");

            migrationBuilder.DropIndex(
                name: "IX_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmission~",
                table: "StatutoryFilingSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_StatutoryFilingPackages_TenantId_PreviousPackageId",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropIndex(
                name: "IX_StatutoryFilingPackages_TenantId_ResubmissionOfSubmissionId",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropIndex(
                name: "IX_StatutoryFilingDefinitions_TenantId_ConnectionProfileId",
                table: "StatutoryFilingDefinitions");

            migrationBuilder.DropColumn(
                name: "ResubmissionOfSubmissionId",
                table: "StatutoryFilingSubmissions");

            migrationBuilder.DropColumn(
                name: "PreviousPackageId",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropColumn(
                name: "ResubmissionOfSubmissionId",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropColumn(
                name: "ConnectionProfileId",
                table: "StatutoryFilingDefinitions");
        }
    }
}
