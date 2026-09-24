using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeCurrentDocumentKeySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_DocumentType_CurrentDocumentKey",
                table: "SeparationGeneratedDocuments");

            migrationBuilder.AlterColumn<string>(
                name: "CurrentDocumentKey",
                table: "SeparationGeneratedDocuments",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_DocumentType_CurrentDocumentKey",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "EmployeeSeparationId", "DocumentType", "CurrentDocumentKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_DocumentType_CurrentDocumentKey",
                table: "SeparationGeneratedDocuments");

            migrationBuilder.AlterColumn<string>(
                name: "CurrentDocumentKey",
                table: "SeparationGeneratedDocuments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_DocumentType_CurrentDocumentKey",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "EmployeeSeparationId", "DocumentType", "CurrentDocumentKey" },
                unique: true,
                filter: "[CurrentDocumentKey] IS NOT NULL");
        }
    }
}
