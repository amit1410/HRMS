using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCurrentSeparationDocumentUniquenessModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentDocumentKey",
                table: "SeparationGeneratedDocuments",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_DocumentType_CurrentDocumentKey",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "EmployeeSeparationId", "DocumentType", "CurrentDocumentKey" },
                unique: true,
                filter: "[CurrentDocumentKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_DocumentType_CurrentDocumentKey",
                table: "SeparationGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "CurrentDocumentKey",
                table: "SeparationGeneratedDocuments");
        }
    }
}
