using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
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
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    PeriodType = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    PayDate = table.Column<DateTime>(type: "date", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    PeriodNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollPeriodHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ChangeType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriodHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollPeriodHistories_PayrollPeriods_TenantId_PayrollPeriod~",
                        columns: x => new { x.TenantId, x.PayrollPeriodId },
                        principalTable: "PayrollPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollPeriodHistories_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    RunType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LockedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollRunEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeSalaryAssignmentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SalaryStructureId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SalaryStructureVersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EmploymentSnapshotDate = table.Column<DateTime>(type: "date", nullable: false),
                    IsEligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExclusionReason = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployees_EmployeeSalaryAssignments_TenantId_Emplo~",
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollRunHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
