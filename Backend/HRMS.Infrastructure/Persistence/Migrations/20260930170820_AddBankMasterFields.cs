using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankMasterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankType",
                table: "Banks",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Banks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "Banks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IfscPrefix",
                table: "Banks",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "Banks",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortName",
                table: "Banks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankType",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "IfscPrefix",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "ShortName",
                table: "Banks");
        }
    }
}
