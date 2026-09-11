using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceApplicabilityRuleName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RuleName",
                table: "ShiftApplicabilityRules",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "Imported applicability rule");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuleName",
                table: "ShiftApplicabilityRules");
        }
    }
}
