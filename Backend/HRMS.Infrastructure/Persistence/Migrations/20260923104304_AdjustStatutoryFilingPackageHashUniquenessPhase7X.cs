using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdjustStatutoryFilingPackageHashUniquenessPhase7X : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StatutoryFilingPackages_TenantId_PackageHash",
                table: "StatutoryFilingPackages");

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingPackages_TenantId_RunId_PackageHash",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "RunId", "PackageHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StatutoryFilingPackages_TenantId_RunId_PackageHash",
                table: "StatutoryFilingPackages");

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryFilingPackages_TenantId_PackageHash",
                table: "StatutoryFilingPackages",
                columns: new[] { "TenantId", "PackageHash" },
                unique: true);
        }
    }
}
