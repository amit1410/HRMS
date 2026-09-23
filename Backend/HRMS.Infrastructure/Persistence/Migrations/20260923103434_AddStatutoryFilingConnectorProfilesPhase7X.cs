using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
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
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousPackageId",
                table: "StatutoryFilingPackages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResubmissionOfSubmissionId",
                table: "StatutoryFilingPackages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionProfileId",
                table: "StatutoryFilingDefinitions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StatutoryFilingConnectionProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ConnectorType = table.Column<int>(type: "int", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NonSecretConfigurationJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SecretReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastValidationStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmissionId",
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
                name: "FK_StatutoryFilingDefinitions_StatutoryFilingConnectionProfiles_TenantId_ConnectionProfileId",
                table: "StatutoryFilingDefinitions",
                columns: new[] { "TenantId", "ConnectionProfileId" },
                principalTable: "StatutoryFilingConnectionProfiles",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingPackages_TenantId_PreviousPackageId",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "PreviousPackageId" },
                principalTable: "StatutoryFilingPackages",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmissionId",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "ResubmissionOfSubmissionId" },
                principalTable: "StatutoryFilingSubmissions",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_StatutoryFilingSubmissions_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmissionId",
                table: "StatutoryFilingSubmissions",
                columns: new[] { "TenantId", "ResubmissionOfSubmissionId" },
                principalTable: "StatutoryFilingSubmissions",
                principalColumns: new[] { "TenantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingDefinitions_StatutoryFilingConnectionProfiles_TenantId_ConnectionProfileId",
                table: "StatutoryFilingDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingPackages_TenantId_PreviousPackageId",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingPackages_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmissionId",
                table: "StatutoryFilingPackages");

            migrationBuilder.DropForeignKey(
                name: "FK_StatutoryFilingSubmissions_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmissionId",
                table: "StatutoryFilingSubmissions");

            migrationBuilder.DropTable(
                name: "StatutoryFilingConnectionProfiles");

            migrationBuilder.DropIndex(
                name: "IX_StatutoryFilingSubmissions_TenantId_ResubmissionOfSubmissionId",
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
