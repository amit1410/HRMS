using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCompOffPhase6D : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompOffPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    AllowWeekOff = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowHoliday = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowOvertimeSource = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EligibilityMode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    MinimumWorkedMinutes = table.Column<int>(type: "int", nullable: false),
                    CreditRatio = table.Column<decimal>(type: "decimal(10,6)", precision: 10, scale: 6, nullable: false),
                    RoundingMode = table.Column<int>(type: "int", nullable: false),
                    RoundingMinutes = table.Column<int>(type: "int", nullable: false),
                    MaximumCreditMinutesPerDay = table.Column<int>(type: "int", nullable: true),
                    MaximumCreditMinutesPerMonth = table.Column<int>(type: "int", nullable: true),
                    ExpiryDays = table.Column<int>(type: "int", nullable: true),
                    ExpiryMonths = table.Column<int>(type: "int", nullable: true),
                    RequireCreditApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowPartialDayConsumption = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConsumptionIncrementMinutes = table.Column<int>(type: "int", nullable: false),
                    BenefitMode = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    TenantId1 = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompOffPolicies", x => x.Id);
                    table.UniqueConstraint("AK_CompOffPolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CompOffPolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffPolicies_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompOffEarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmploymentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceWorkDate = table.Column<DateTime>(type: "date", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceAttendanceDayId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceAttendanceSnapshotId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceAttendanceVersion = table.Column<int>(type: "int", nullable: false),
                    SourceOvertimeRequestId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceOvertimeSnapshotId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PolicyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    SourceWorkedMinutes = table.Column<int>(type: "int", nullable: false),
                    EligibleMinutes = table.Column<int>(type: "int", nullable: false),
                    CreditedMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "date", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ApprovedByEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceKey = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false),
                    TenantId1 = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompOffEarnings", x => x.Id);
                    table.UniqueConstraint("AK_CompOffEarnings_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CompOffEarnings_CompOffPolicies_TenantId_PolicyId",
                        columns: x => new { x.TenantId, x.PolicyId },
                        principalTable: "CompOffPolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffEarnings_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffEarnings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffEarnings_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompOffLeaveAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EarningId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReservedMinutes = table.Column<int>(type: "int", nullable: false),
                    ConsumedMinutes = table.Column<int>(type: "int", nullable: false),
                    ReleasedMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    TenantId1 = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompOffLeaveAllocations", x => x.Id);
                    table.UniqueConstraint("AK_CompOffLeaveAllocations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CompOffLeaveAllocations_CompOffEarnings_TenantId_EarningId",
                        columns: x => new { x.TenantId, x.EarningId },
                        principalTable: "CompOffEarnings",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffLeaveAllocations_LeaveRequests_TenantId_LeaveRequestId",
                        columns: x => new { x.TenantId, x.LeaveRequestId },
                        principalTable: "LeaveRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffLeaveAllocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffLeaveAllocations_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompOffLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EarningId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "date", nullable: true),
                    SourceReference = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    CorrelationId = table.Column<string>(type: "longtext", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ActorEmployeeId = table.Column<Guid>(type: "char(36)", nullable: true),
                    TenantId1 = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompOffLedgerEntries", x => x.Id);
                    table.UniqueConstraint("AK_CompOffLedgerEntries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CompOffLedgerEntries_CompOffEarnings_TenantId_EarningId",
                        columns: x => new { x.TenantId, x.EarningId },
                        principalTable: "CompOffEarnings",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffLedgerEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompOffLedgerEntries_Tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CompOffEarnings_TenantId_EmployeeId_Status_ExpiresOn",
                table: "CompOffEarnings",
                columns: new[] { "TenantId", "EmployeeId", "Status", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CompOffEarnings_TenantId_PolicyId",
                table: "CompOffEarnings",
                columns: new[] { "TenantId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompOffEarnings_TenantId_SourceKey",
                table: "CompOffEarnings",
                columns: new[] { "TenantId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompOffEarnings_TenantId1",
                table: "CompOffEarnings",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLeaveAllocations_TenantId_EarningId",
                table: "CompOffLeaveAllocations",
                columns: new[] { "TenantId", "EarningId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLeaveAllocations_TenantId_LeaveRequestId_EarningId",
                table: "CompOffLeaveAllocations",
                columns: new[] { "TenantId", "LeaveRequestId", "EarningId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLeaveAllocations_TenantId1",
                table: "CompOffLeaveAllocations",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLedgerEntries_TenantId_EarningId",
                table: "CompOffLedgerEntries",
                columns: new[] { "TenantId", "EarningId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLedgerEntries_TenantId_EmployeeId_EffectiveDate",
                table: "CompOffLedgerEntries",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLedgerEntries_TenantId_IdempotencyKey",
                table: "CompOffLedgerEntries",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompOffLedgerEntries_TenantId1",
                table: "CompOffLedgerEntries",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_CompOffPolicies_TenantId_Code",
                table: "CompOffPolicies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompOffPolicies_TenantId_EffectiveFrom_EffectiveTo",
                table: "CompOffPolicies",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_CompOffPolicies_TenantId1",
                table: "CompOffPolicies",
                column: "TenantId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompOffLeaveAllocations");

            migrationBuilder.DropTable(
                name: "CompOffLedgerEntries");

            migrationBuilder.DropTable(
                name: "CompOffEarnings");

            migrationBuilder.DropTable(
                name: "CompOffPolicies");
        }
    }
}
