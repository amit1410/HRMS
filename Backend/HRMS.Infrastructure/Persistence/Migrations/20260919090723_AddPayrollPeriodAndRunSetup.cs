using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollPeriodAndRunSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PeriodType = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    PeriodNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriods", x => x.Id);
                    table.UniqueConstraint("AK_PayrollPeriods_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollPeriods_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollPeriodHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriodHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollPeriodHistories_PayrollPeriods_TenantId_PayrollPeriodId",
                        columns: x => new { x.TenantId, x.PayrollPeriodId },
                        principalTable: "PayrollPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollPeriodHistories_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RunType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.UniqueConstraint("AK_PayrollRuns_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayrollRuns_PayrollPeriods_TenantId_PayrollPeriodId",
                        columns: x => new { x.TenantId, x.PayrollPeriodId },
                        principalTable: "PayrollPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeSalaryAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalaryStructureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalaryStructureVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmploymentSnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsEligible = table.Column<bool>(type: "bit", nullable: false),
                    ExclusionReason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployees_EmployeeSalaryAssignments_TenantId_EmployeeSalaryAssignmentId",
                        columns: x => new { x.TenantId, x.EmployeeSalaryAssignmentId },
                        principalTable: "EmployeeSalaryAssignments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployees_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployees_PayrollRuns_TenantId_PayrollRunId",
                        columns: x => new { x.TenantId, x.PayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunHistories_PayrollRuns_TenantId_PayrollRunId",
                        columns: x => new { x.TenantId, x.PayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRunHistories_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriodHistories_TenantId_ActorUserId",
                table: "PayrollPeriodHistories",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriodHistories_TenantId_PayrollPeriodId_ChangedAtUtc",
                table: "PayrollPeriodHistories",
                columns: new[] { "TenantId", "PayrollPeriodId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_TenantId_Code",
                table: "PayrollPeriods",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_TenantId_StartDate_EndDate",
                table: "PayrollPeriods",
                columns: new[] { "TenantId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_TenantId_Status",
                table: "PayrollPeriods",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_EmployeeSalaryAssignmentId",
                table: "PayrollRunEmployees",
                column: "EmployeeSalaryAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_TenantId_EmployeeId",
                table: "PayrollRunEmployees",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_TenantId_EmployeeSalaryAssignmentId",
                table: "PayrollRunEmployees",
                columns: new[] { "TenantId", "EmployeeSalaryAssignmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_TenantId_PayrollRunId_EmployeeId",
                table: "PayrollRunEmployees",
                columns: new[] { "TenantId", "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunHistories_TenantId_ActorUserId",
                table: "PayrollRunHistories",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunHistories_TenantId_PayrollRunId_ChangedAtUtc",
                table: "PayrollRunHistories",
                columns: new[] { "TenantId", "PayrollRunId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_TenantId_PayrollPeriodId",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "PayrollPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_TenantId_RunNumber",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_TenantId_Status",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollPeriodHistories");

            migrationBuilder.DropTable(
                name: "PayrollRunEmployees");

            migrationBuilder.DropTable(
                name: "PayrollRunHistories");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "PayrollPeriods");
        }
    }
}
