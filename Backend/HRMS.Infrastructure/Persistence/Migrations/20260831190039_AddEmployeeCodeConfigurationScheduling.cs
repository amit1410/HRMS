using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCodeConfigurationScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "EmployeeCodeConfigs",
                type: "date",
                nullable: false,
                defaultValueSql: "CONVERT(date, GETUTCDATE())");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveTo",
                table: "EmployeeCodeConfigs",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Separator",
                table: "EmployeeCodeConfigs",
                type: "nvarchar(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "-");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "EmployeeCodeConfigs");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                table: "EmployeeCodeConfigs");

            migrationBuilder.DropColumn(
                name: "Separator",
                table: "EmployeeCodeConfigs");
        }
    }
}
