using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    public partial class AddLeavePeriodCloseOccurrencesV2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeavePeriodCloseOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SourceLeavePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DestinationLeavePeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OccurrenceKey = table.Column<string>(type: "varchar(220)", maxLength: 220, nullable: false),
                    ClosingQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CarriedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    LapsedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ClaimToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePeriodCloseOccurrences", x => x.Id);
                    table.UniqueConstraint("AK_LeavePeriodCloseOccurrences_TenantId_Id", x => new { x.TenantId, x.Id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex("IX_LeavePeriodCloseOccurrences_TenantId_OccurrenceKey", "LeavePeriodCloseOccurrences", new[] { "TenantId", "OccurrenceKey" }, unique: true);
            migrationBuilder.CreateIndex("IX_LeavePeriodCloseOccurrences_TenantId_Status_SourceLeavePerio~", "LeavePeriodCloseOccurrences", new[] { "TenantId", "Status", "SourceLeavePeriodId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LeavePeriodCloseOccurrences");
        }
    }
}
