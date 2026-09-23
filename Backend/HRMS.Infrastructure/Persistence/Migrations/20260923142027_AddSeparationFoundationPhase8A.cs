using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationFoundationPhase8A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeparationReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    EmployeeInitiatedAllowed = table.Column<bool>(type: "bit", nullable: false),
                    EmployerInitiatedAllowed = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationReasons", x => x.Id);
                    table.UniqueConstraint("AK_SeparationReasons_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationReasons_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeSeparations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActiveEmployeeKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SeparationNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SeparationType = table.Column<int>(type: "int", nullable: false),
                    ReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitiatedBy = table.Column<int>(type: "int", nullable: false),
                    InitiatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProposedLastWorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ApprovedLastWorkingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticeStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticeEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticePeriodDays = table.Column<int>(type: "int", nullable: true),
                    NoticeServedDays = table.Column<int>(type: "int", nullable: true),
                    NoticeShortfallDays = table.Column<int>(type: "int", nullable: true),
                    EmployeeRemarks = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ManagerRemarks = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    HrRemarks = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSeparations", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeSeparations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeSeparations_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSeparations_SeparationReasons_TenantId_ReasonId",
                        columns: x => new { x.TenantId, x.ReasonId },
                        principalTable: "SeparationReasons",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSeparations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeSeparationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeSeparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSeparationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeSeparationEvents_EmployeeSeparations_TenantId_EmployeeSeparationId",
                        columns: x => new { x.TenantId, x.EmployeeSeparationId },
                        principalTable: "EmployeeSeparations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeSeparationEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSeparationEvents_TenantId_EmployeeSeparationId_OccurredAtUtc",
                table: "EmployeeSeparationEvents",
                columns: new[] { "TenantId", "EmployeeSeparationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSeparations_TenantId_ActiveEmployeeKey",
                table: "EmployeeSeparations",
                columns: new[] { "TenantId", "ActiveEmployeeKey" },
                unique: true,
                filter: "[ActiveEmployeeKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSeparations_TenantId_EmployeeId_Status",
                table: "EmployeeSeparations",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSeparations_TenantId_ReasonId",
                table: "EmployeeSeparations",
                columns: new[] { "TenantId", "ReasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSeparations_TenantId_SeparationNumber",
                table: "EmployeeSeparations",
                columns: new[] { "TenantId", "SeparationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationReasons_TenantId_Code",
                table: "SeparationReasons",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeSeparationEvents");

            migrationBuilder.DropTable(
                name: "EmployeeSeparations");

            migrationBuilder.DropTable(
                name: "SeparationReasons");
        }
    }
}
