using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkDocumentsToPreviousEmployment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreviousEmploymentId",
                table: "EmployeeDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EmployeePreviousEmployments_TenantId_Id",
                table: "EmployeePreviousEmployments",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_TenantId_PreviousEmploymentId",
                table: "EmployeeDocuments",
                columns: new[] { "TenantId", "PreviousEmploymentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDocuments_EmployeePreviousEmployments_TenantId_PreviousEmploymentId",
                table: "EmployeeDocuments",
                columns: new[] { "TenantId", "PreviousEmploymentId" },
                principalTable: "EmployeePreviousEmployments",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDocuments_EmployeePreviousEmployments_TenantId_PreviousEmploymentId",
                table: "EmployeeDocuments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EmployeePreviousEmployments_TenantId_Id",
                table: "EmployeePreviousEmployments");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDocuments_TenantId_PreviousEmploymentId",
                table: "EmployeeDocuments");

            migrationBuilder.DropColumn(
                name: "PreviousEmploymentId",
                table: "EmployeeDocuments");
        }
    }
}
