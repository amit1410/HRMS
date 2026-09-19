using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollOutputsPayslipFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payslips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollResultId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayrollRunEmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayslipNumber = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    PeriodStartDate = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodEndDate = table.Column<DateTime>(type: "date", nullable: false),
                    PayDate = table.Column<DateTime>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EmployerContributionTotal = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    EmployeeCode = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    EmployeeName = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    Designation = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: true),
                    Department = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: true),
                    WorkLocation = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: true),
                    DateOfJoining = table.Column<DateTime>(type: "date", nullable: true),
                    SalaryStructureReference = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payslips", x => x.Id);
                    table.UniqueConstraint("AK_Payslips_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Payslips_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payslips_PayrollResults_TenantId_PayrollResultId",
                        columns: x => new { x.TenantId, x.PayrollResultId },
                        principalTable: "PayrollResults",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payslips_PayrollRunEmployees_TenantId_PayrollRunEmployeeId",
                        columns: x => new { x.TenantId, x.PayrollRunEmployeeId },
                        principalTable: "PayrollRunEmployees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payslips_PayrollRuns_TenantId_PayrollRunId",
                        columns: x => new { x.TenantId, x.PayrollRunId },
                        principalTable: "PayrollRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payslips_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayslipHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayslipId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayslipHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayslipHistories_Payslips_TenantId_PayslipId",
                        columns: x => new { x.TenantId, x.PayslipId },
                        principalTable: "Payslips",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayslipHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayslipHistories_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayslipLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayslipId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ComponentCode = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    ComponentName = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false),
                    ComponentType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    DisplayGroup = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    IsStatutory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EmployerAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayslipLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayslipLines_Payslips_TenantId_PayslipId",
                        columns: x => new { x.TenantId, x.PayslipId },
                        principalTable: "Payslips",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayslipLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PayslipHistories_TenantId_ActorUserId",
                table: "PayslipHistories",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayslipHistories_TenantId_PayslipId_ChangedAtUtc",
                table: "PayslipHistories",
                columns: new[] { "TenantId", "PayslipId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayslipLines_TenantId_PayslipId_Sequence",
                table: "PayslipLines",
                columns: new[] { "TenantId", "PayslipId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_TenantId_EmployeeId_PeriodEndDate",
                table: "Payslips",
                columns: new[] { "TenantId", "EmployeeId", "PeriodEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_TenantId_PayrollResultId",
                table: "Payslips",
                columns: new[] { "TenantId", "PayrollResultId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_TenantId_PayrollRunEmployeeId",
                table: "Payslips",
                columns: new[] { "TenantId", "PayrollRunEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_TenantId_PayrollRunId",
                table: "Payslips",
                columns: new[] { "TenantId", "PayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_TenantId_PayslipNumber",
                table: "Payslips",
                columns: new[] { "TenantId", "PayslipNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayslipHistories");

            migrationBuilder.DropTable(
                name: "PayslipLines");

            migrationBuilder.DropTable(
                name: "Payslips");
        }
    }
}
