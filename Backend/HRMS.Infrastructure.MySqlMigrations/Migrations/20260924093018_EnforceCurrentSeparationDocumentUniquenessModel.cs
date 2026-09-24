using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCurrentSeparationDocumentUniquenessModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_D~",
                table: "SeparationGeneratedDocuments",
                newName: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_~1");

            migrationBuilder.AddColumn<string>(
                name: "CurrentDocumentKey",
                table: "SeparationGeneratedDocuments",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_D~",
                table: "SeparationGeneratedDocuments",
                columns: new[] { "TenantId", "EmployeeSeparationId", "DocumentType", "CurrentDocumentKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_D~",
                table: "SeparationGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "CurrentDocumentKey",
                table: "SeparationGeneratedDocuments");

            migrationBuilder.RenameIndex(
                name: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_~1",
                table: "SeparationGeneratedDocuments",
                newName: "IX_SeparationGeneratedDocuments_TenantId_EmployeeSeparationId_D~");
        }
    }
}
