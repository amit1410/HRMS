using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRegularizationAndOnDuty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceOnDutyRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewerComments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceOnDutyRequests", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceOnDutyRequests_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceOnDutyRequests_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceOnDutyRequests_Users_TenantId_SubmittedByUserId",
                        columns: x => new { x.TenantId, x.SubmittedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRegularizationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ProposedInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProposedOutAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewerComments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRegularizationRequests", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceRegularizationRequests_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceRegularizationRequests_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRegularizationRequests_Users_TenantId_SubmittedByUserId",
                        columns: x => new { x.TenantId, x.SubmittedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceOnDutyEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceOnDutyRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Comments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceOnDutyEvents", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceOnDutyEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceOnDutyEvents_AttendanceOnDutyRequests_TenantId_AttendanceOnDutyRequestId",
                        columns: x => new { x.TenantId, x.AttendanceOnDutyRequestId },
                        principalTable: "AttendanceOnDutyRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceOnDutyEvents_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AttendanceRegularizationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveOutAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAdjustments", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceAdjustments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_AttendanceRegularizationRequests_TenantId_AttendanceRegularizationRequestId",
                        columns: x => new { x.TenantId, x.AttendanceRegularizationRequestId },
                        principalTable: "AttendanceRegularizationRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_Users_TenantId_ApprovedByUserId",
                        columns: x => new { x.TenantId, x.ApprovedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRegularizationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceRegularizationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Comments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRegularizationEvents", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceRegularizationEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceRegularizationEvents_AttendanceRegularizationRequests_TenantId_AttendanceRegularizationRequestId",
                        columns: x => new { x.TenantId, x.AttendanceRegularizationRequestId },
                        principalTable: "AttendanceRegularizationRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRegularizationEvents_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_TenantId_ApprovedByUserId",
                table: "AttendanceAdjustments",
                columns: new[] { "TenantId", "ApprovedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_TenantId_AttendanceRegularizationRequestId",
                table: "AttendanceAdjustments",
                columns: new[] { "TenantId", "AttendanceRegularizationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_TenantId_EmployeeId_BusinessDate",
                table: "AttendanceAdjustments",
                columns: new[] { "TenantId", "EmployeeId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceOnDutyEvents_TenantId_ActorUserId",
                table: "AttendanceOnDutyEvents",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceOnDutyEvents_TenantId_AttendanceOnDutyRequestId_OccurredAtUtc",
                table: "AttendanceOnDutyEvents",
                columns: new[] { "TenantId", "AttendanceOnDutyRequestId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceOnDutyRequests_TenantId_EmployeeId_StartDate_EndDate_Status",
                table: "AttendanceOnDutyRequests",
                columns: new[] { "TenantId", "EmployeeId", "StartDate", "EndDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceOnDutyRequests_TenantId_SubmittedByUserId",
                table: "AttendanceOnDutyRequests",
                columns: new[] { "TenantId", "SubmittedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegularizationEvents_TenantId_ActorUserId",
                table: "AttendanceRegularizationEvents",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegularizationEvents_TenantId_AttendanceRegularizationRequestId_OccurredAtUtc",
                table: "AttendanceRegularizationEvents",
                columns: new[] { "TenantId", "AttendanceRegularizationRequestId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegularizationRequests_TenantId_EmployeeId_BusinessDate_Status",
                table: "AttendanceRegularizationRequests",
                columns: new[] { "TenantId", "EmployeeId", "BusinessDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegularizationRequests_TenantId_SubmittedByUserId",
                table: "AttendanceRegularizationRequests",
                columns: new[] { "TenantId", "SubmittedByUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceAdjustments");

            migrationBuilder.DropTable(
                name: "AttendanceOnDutyEvents");

            migrationBuilder.DropTable(
                name: "AttendanceRegularizationEvents");

            migrationBuilder.DropTable(
                name: "AttendanceOnDutyRequests");

            migrationBuilder.DropTable(
                name: "AttendanceRegularizationRequests");
        }
    }
}
