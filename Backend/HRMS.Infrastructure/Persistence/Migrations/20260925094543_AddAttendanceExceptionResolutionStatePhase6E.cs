using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceExceptionResolutionStatePhase6E : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceExceptionResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceDayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceVersion = table.Column<int>(type: "int", nullable: false),
                    ExceptionType = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceExceptionResolutions", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceExceptionResolutions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceExceptionResolutions_EmployeeAttendanceDays_TenantId_AttendanceDayId",
                        columns: x => new { x.TenantId, x.AttendanceDayId },
                        principalTable: "EmployeeAttendanceDays",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceExceptionResolutions_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceExceptionResolutions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionResolutions_TenantId_AttendanceDayId_AttendanceVersion_ExceptionType",
                table: "AttendanceExceptionResolutions",
                columns: new[] { "TenantId", "AttendanceDayId", "AttendanceVersion", "ExceptionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionResolutions_TenantId_EmployeeId_ExceptionType_AttendanceVersion",
                table: "AttendanceExceptionResolutions",
                columns: new[] { "TenantId", "EmployeeId", "ExceptionType", "AttendanceVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceExceptionResolutions");
        }
    }
}
