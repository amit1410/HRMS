using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeavePeriodCloseOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeavePeriodCloseOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLeavePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationLeavePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeavePolicyRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurrenceKey = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    ClosingQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CarriedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    LapsedQuantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClaimToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePeriodCloseOccurrences", x => x.Id);
                    table.UniqueConstraint("AK_LeavePeriodCloseOccurrences_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePeriodCloseOccurrences_TenantId_OccurrenceKey",
                table: "LeavePeriodCloseOccurrences",
                columns: new[] { "TenantId", "OccurrenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePeriodCloseOccurrences_TenantId_Status_SourceLeavePeriodId",
                table: "LeavePeriodCloseOccurrences",
                columns: new[] { "TenantId", "Status", "SourceLeavePeriodId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeavePeriodCloseOccurrences");
        }
    }
}
