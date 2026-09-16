using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRMS.Infrastructure.Persistence;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations;

[DbContext(typeof(HrmsDbContext))]
[Migration("20260913130001_AddEmployeeNoticePeriod")]
public partial class AddEmployeeNoticePeriod : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "NoticeEndDate", table: "EmployeeEmployments", type: "date", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "NoticeStartDate", table: "EmployeeEmployments", type: "date", nullable: true);
        migrationBuilder.AddColumn<int>(name: "NoticeStatus", table: "EmployeeEmployments", type: "int", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "NoticeEndDate", table: "EmployeeEmployments");
        migrationBuilder.DropColumn(name: "NoticeStartDate", table: "EmployeeEmployments");
        migrationBuilder.DropColumn(name: "NoticeStatus", table: "EmployeeEmployments");
    }
}
