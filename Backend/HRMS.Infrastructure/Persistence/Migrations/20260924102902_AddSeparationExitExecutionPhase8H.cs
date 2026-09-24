using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationExitExecutionPhase8H : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeparationExitExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeSeparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinalLastWorkingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmploymentExecutedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AccessDeprovisionedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 12000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationExitExecutions_EmployeeSeparations_TenantId_EmployeeSeparationId",
                        columns: x => new { x.TenantId, x.EmployeeSeparationId },
                        principalTable: "EmployeeSeparations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitExecutions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeparationExitExecutionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeparationExitExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationExitExecutionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationExitExecutionEvents_SeparationExitExecutions_SeparationExitExecutionId",
                        column: x => x.SeparationExitExecutionId,
                        principalTable: "SeparationExitExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationExitExecutionEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitExecutionEvents_SeparationExitExecutionId",
                table: "SeparationExitExecutionEvents",
                column: "SeparationExitExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitExecutionEvents_TenantId_SeparationExitExecutionId_EventType",
                table: "SeparationExitExecutionEvents",
                columns: new[] { "TenantId", "SeparationExitExecutionId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitExecutions_TenantId_EmployeeSeparationId",
                table: "SeparationExitExecutions",
                columns: new[] { "TenantId", "EmployeeSeparationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationExitExecutions_TenantId_Status",
                table: "SeparationExitExecutions",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeparationExitExecutionEvents");

            migrationBuilder.DropTable(
                name: "SeparationExitExecutions");
        }
    }
}
