using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceDeviceAdministrationAuditPhase6F : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceDeviceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    AttendanceDeviceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    AttendanceDeviceEmployeeMappingId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Action = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ContextJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDeviceAuditEvents", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceDeviceAuditEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceAuditEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceAuditEvents_TenantId_AttendanceDeviceEmploye~",
                table: "AttendanceDeviceAuditEvents",
                columns: new[] { "TenantId", "AttendanceDeviceEmployeeMappingId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceAuditEvents_TenantId_AttendanceDeviceId_Occu~",
                table: "AttendanceDeviceAuditEvents",
                columns: new[] { "TenantId", "AttendanceDeviceId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceDeviceAuditEvents");
        }
    }
}
