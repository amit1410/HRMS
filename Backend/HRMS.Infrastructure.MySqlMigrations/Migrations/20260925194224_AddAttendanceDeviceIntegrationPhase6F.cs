using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceDeviceIntegrationPhase6F : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    DeviceType = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Vendor = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    TimeZoneId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ConnectionMode = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CredentialReference = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    LastSuccessfulCheckpoint = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    LastSuccessfulSyncAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastAttemptedSyncAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDevices", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceDevices_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceDevices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDevices_WorkLocations_TenantId_WorkLocationId",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendanceDeviceEmployeeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceDeviceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ExternalEmployeeIdentifier = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDeviceEmployeeMappings", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceDeviceEmployeeMappings_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceEmployeeMappings_AttendanceDevices_TenantId_~",
                        columns: x => new { x.TenantId, x.AttendanceDeviceId },
                        principalTable: "AttendanceDevices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceEmployeeMappings_Employees_TenantId_Employee~",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendanceDeviceSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceDeviceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Source = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReceivedCount = table.Column<int>(type: "int", nullable: false),
                    AcceptedCount = table.Column<int>(type: "int", nullable: false),
                    DuplicateCount = table.Column<int>(type: "int", nullable: false),
                    RejectedCount = table.Column<int>(type: "int", nullable: false),
                    UnmappedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    CheckpointBefore = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CheckpointAfter = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDeviceSyncRuns", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceDeviceSyncRuns_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceSyncRuns_AttendanceDevices_TenantId_Attendan~",
                        columns: x => new { x.TenantId, x.AttendanceDeviceId },
                        principalTable: "AttendanceDevices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendanceDeviceIngestionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceDeviceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    AttendanceDeviceSyncRunId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    AttendancePunchId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Source = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ExternalEventId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ExternalEmployeeIdentifier = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SanitizedError = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDeviceIngestionEvents", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceDeviceIngestionEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceIngestionEvents_AttendanceDeviceSyncRuns_Ten~",
                        columns: x => new { x.TenantId, x.AttendanceDeviceSyncRunId },
                        principalTable: "AttendanceDeviceSyncRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceIngestionEvents_AttendanceDevices_TenantId_A~",
                        columns: x => new { x.TenantId, x.AttendanceDeviceId },
                        principalTable: "AttendanceDevices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceIngestionEvents_AttendancePunches_TenantId_A~",
                        columns: x => new { x.TenantId, x.AttendancePunchId },
                        principalTable: "AttendancePunches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDeviceIngestionEvents_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceEmployeeMappings_TenantId_AttendanceDeviceI~1",
                table: "AttendanceDeviceEmployeeMappings",
                columns: new[] { "TenantId", "AttendanceDeviceId", "ExternalEmployeeIdentifier", "Status", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceEmployeeMappings_TenantId_AttendanceDeviceId~",
                table: "AttendanceDeviceEmployeeMappings",
                columns: new[] { "TenantId", "AttendanceDeviceId", "ExternalEmployeeIdentifier", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceEmployeeMappings_TenantId_EmployeeId",
                table: "AttendanceDeviceEmployeeMappings",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceIngestionEvents_TenantId_AttendanceDeviceId",
                table: "AttendanceDeviceIngestionEvents",
                columns: new[] { "TenantId", "AttendanceDeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceIngestionEvents_TenantId_AttendanceDeviceSyn~",
                table: "AttendanceDeviceIngestionEvents",
                columns: new[] { "TenantId", "AttendanceDeviceSyncRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceIngestionEvents_TenantId_AttendancePunchId",
                table: "AttendanceDeviceIngestionEvents",
                columns: new[] { "TenantId", "AttendancePunchId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceIngestionEvents_TenantId_EmployeeId",
                table: "AttendanceDeviceIngestionEvents",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceIngestionEvents_TenantId_Source_ExternalEven~",
                table: "AttendanceDeviceIngestionEvents",
                columns: new[] { "TenantId", "Source", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceIngestionEvents_TenantId_Status_ReceivedAtUtc",
                table: "AttendanceDeviceIngestionEvents",
                columns: new[] { "TenantId", "Status", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevices_TenantId_Code",
                table: "AttendanceDevices",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevices_TenantId_WorkLocationId",
                table: "AttendanceDevices",
                columns: new[] { "TenantId", "WorkLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceSyncRuns_TenantId_AttendanceDeviceId_Started~",
                table: "AttendanceDeviceSyncRuns",
                columns: new[] { "TenantId", "AttendanceDeviceId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceDeviceEmployeeMappings");

            migrationBuilder.DropTable(
                name: "AttendanceDeviceIngestionEvents");

            migrationBuilder.DropTable(
                name: "AttendanceDeviceSyncRuns");

            migrationBuilder.DropTable(
                name: "AttendanceDevices");
        }
    }
}
