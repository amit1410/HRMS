using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttendanceDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttendanceDeviceEmployeeMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContextJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceAuditEvents_TenantId_AttendanceDeviceEmployeeMappingId_OccurredAtUtc",
                table: "AttendanceDeviceAuditEvents",
                columns: new[] { "TenantId", "AttendanceDeviceEmployeeMappingId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceAuditEvents_TenantId_AttendanceDeviceId_OccurredAtUtc",
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
