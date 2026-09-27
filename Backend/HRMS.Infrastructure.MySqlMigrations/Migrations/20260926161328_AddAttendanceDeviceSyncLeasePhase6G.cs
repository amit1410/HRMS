using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceDeviceSyncLeasePhase6G : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceDeviceSyncLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceDeviceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    LeaseToken = table.Column<Guid>(type: "char(36)", nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDeviceSyncLeases", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceDeviceSyncLeases_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceSyncLeases_AttendanceDevices_TenantId_Attend~",
                        columns: x => new { x.TenantId, x.AttendanceDeviceId },
                        principalTable: "AttendanceDevices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceSyncLeases_TenantId_AttendanceDeviceId",
                table: "AttendanceDeviceSyncLeases",
                columns: new[] { "TenantId", "AttendanceDeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceDeviceSyncLeases");
        }
    }
}
