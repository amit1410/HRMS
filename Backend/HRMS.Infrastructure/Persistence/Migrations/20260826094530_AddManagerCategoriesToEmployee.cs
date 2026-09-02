using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagerCategoriesToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ManagerCategories",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Seed all existing active employees with all supervisor categories (L1|L2|L3|Other|HR|Time = 63)
            // so the supervisor selection feature works immediately without a manual data entry pass.
            migrationBuilder.Sql("UPDATE Employees SET ManagerCategories = 63 WHERE ManagerCategories = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagerCategories",
                table: "Employees");
        }
    }
}
