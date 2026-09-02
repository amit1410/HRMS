using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankMasterAndEmployeeBankRefinements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankName",
                table: "EmployeeBankDetails");

            migrationBuilder.AddColumn<Guid>(
                name: "BankId",
                table: "EmployeeBankDetails",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "BranchName",
                table: "EmployeeBankDetails",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "EmployeeBankDetails",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "EmployeeBankDetails",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "EmployeeBankDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Banks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id);
                    table.UniqueConstraint("AK_Banks_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Banks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBankDetails_TenantId_BankId",
                table: "EmployeeBankDetails",
                columns: new[] { "TenantId", "BankId" });

            migrationBuilder.CreateIndex(
                name: "IX_Banks_TenantId_Code",
                table: "Banks",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Banks_TenantId_Name",
                table: "Banks",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBankDetails_Banks_TenantId_BankId",
                table: "EmployeeBankDetails",
                columns: new[] { "TenantId", "BankId" },
                principalTable: "Banks",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBankDetails_Banks_TenantId_BankId",
                table: "EmployeeBankDetails");

            migrationBuilder.DropTable(
                name: "Banks");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeBankDetails_TenantId_BankId",
                table: "EmployeeBankDetails");

            migrationBuilder.DropColumn(
                name: "BankId",
                table: "EmployeeBankDetails");

            migrationBuilder.DropColumn(
                name: "BranchName",
                table: "EmployeeBankDetails");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "EmployeeBankDetails");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "EmployeeBankDetails");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "EmployeeBankDetails");

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "EmployeeBankDetails",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
