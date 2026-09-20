using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollControlsProductionReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "FinalSettlementCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockReason",
                table: "PayrollPeriods",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAtUtc",
                table: "PayrollPeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LockedByUserId",
                table: "PayrollPeriods",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnlockReason",
                table: "PayrollPeriods",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnlockedAtUtc",
                table: "PayrollPeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnlockedByUserId",
                table: "PayrollPeriods",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollControlConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequireMakerChecker = table.Column<bool>(type: "bit", nullable: false),
                    PreventSelfApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireReasonForReopen = table.Column<bool>(type: "bit", nullable: false),
                    RequireReasonForCancellation = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollControlConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollControlConfigurations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollControlConfigurations_TenantId",
                table: "PayrollControlConfigurations",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "FinalSettlementCases");

            migrationBuilder.DropTable(
                name: "PayrollControlConfigurations");

            migrationBuilder.DropColumn(
                name: "LockReason",
                table: "PayrollPeriods");

            migrationBuilder.DropColumn(
                name: "LockedAtUtc",
                table: "PayrollPeriods");

            migrationBuilder.DropColumn(
                name: "LockedByUserId",
                table: "PayrollPeriods");

            migrationBuilder.DropColumn(
                name: "UnlockReason",
                table: "PayrollPeriods");

            migrationBuilder.DropColumn(
                name: "UnlockedAtUtc",
                table: "PayrollPeriods");

            migrationBuilder.DropColumn(
                name: "UnlockedByUserId",
                table: "PayrollPeriods");
        }
    }
}
