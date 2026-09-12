using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRMS.Infrastructure.Persistence;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20260912090001_AddAttendanceWorkflowConcurrencyVersion")]
public partial class AddAttendanceWorkflowConcurrencyVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ConcurrencyVersion",
            table: "AttendanceRegularizationRequests",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "ConcurrencyVersion",
            table: "AttendanceOnDutyRequests",
            type: "int",
            nullable: false,
            defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ConcurrencyVersion",
            table: "AttendanceRegularizationRequests");

        migrationBuilder.DropColumn(
            name: "ConcurrencyVersion",
            table: "AttendanceOnDutyRequests");
    }
}
