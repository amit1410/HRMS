using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationClearancePhase8D : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeparationClearances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeSeparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationClearances", x => x.Id);
                    table.UniqueConstraint("AK_SeparationClearances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationClearances_EmployeeSeparations_TenantId_EmployeeSeparationId",
                        columns: x => new { x.TenantId, x.EmployeeSeparationId },
                        principalTable: "EmployeeSeparations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationClearances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationClearanceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    AppliesToSeparationType = table.Column<int>(type: "int", nullable: true),
                    AppliesToReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationClearanceTemplates", x => x.Id);
                    table.UniqueConstraint("AK_SeparationClearanceTemplates_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationClearanceTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationClearanceEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeparationClearanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: true),
                    FromTaskStatus = table.Column<int>(type: "int", nullable: true),
                    ToTaskStatus = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_SeparationClearanceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationClearanceEvents_SeparationClearances_TenantId_SeparationClearanceId",
                        columns: x => new { x.TenantId, x.SeparationClearanceId },
                        principalTable: "SeparationClearances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationClearanceEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationClearanceTemplateItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    OwnerReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    RequiresAssetReturn = table.Column<bool>(type: "bit", nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false),
                    RequiresEvidence = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DueDaysBeforeLwd = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationClearanceTemplateItems", x => x.Id);
                    table.UniqueConstraint("AK_SeparationClearanceTemplateItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationClearanceTemplateItems_SeparationClearanceTemplates_TenantId_TemplateId",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "SeparationClearanceTemplates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeparationClearanceTemplateItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationClearanceTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeparationClearanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    RequiresAssetReturn = table.Column<bool>(type: "bit", nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false),
                    RequiresEvidence = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    BlockingReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationClearanceTasks", x => x.Id);
                    table.UniqueConstraint("AK_SeparationClearanceTasks_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SeparationClearanceTasks_SeparationClearanceTemplateItems_TenantId_TemplateItemId",
                        columns: x => new { x.TenantId, x.TemplateItemId },
                        principalTable: "SeparationClearanceTemplateItems",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationClearanceTasks_SeparationClearances_TenantId_SeparationClearanceId",
                        columns: x => new { x.TenantId, x.SeparationClearanceId },
                        principalTable: "SeparationClearances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationClearanceTasks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeparationAssetReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeparationClearanceTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetReference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AssetType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AssetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpectedReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReturnStatus = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    RecoveryRequired = table.Column<bool>(type: "bit", nullable: false),
                    RecoveryReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationAssetReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationAssetReturns_SeparationClearanceTasks_TenantId_SeparationClearanceTaskId",
                        columns: x => new { x.TenantId, x.SeparationClearanceTaskId },
                        principalTable: "SeparationClearanceTasks",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeparationAssetReturns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationAssetReturns_TenantId_EmployeeId_ReturnStatus",
                table: "SeparationAssetReturns",
                columns: new[] { "TenantId", "EmployeeId", "ReturnStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationAssetReturns_TenantId_SeparationClearanceTaskId",
                table: "SeparationAssetReturns",
                columns: new[] { "TenantId", "SeparationClearanceTaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceEvents_TenantId_SeparationClearanceId_OccurredAtUtc",
                table: "SeparationClearanceEvents",
                columns: new[] { "TenantId", "SeparationClearanceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceEvents_TenantId_TaskId_OccurredAtUtc",
                table: "SeparationClearanceEvents",
                columns: new[] { "TenantId", "TaskId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearances_TenantId_EmployeeSeparationId",
                table: "SeparationClearances",
                columns: new[] { "TenantId", "EmployeeSeparationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearances_TenantId_Status",
                table: "SeparationClearances",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTasks_TenantId_AssignedDepartmentId_Status",
                table: "SeparationClearanceTasks",
                columns: new[] { "TenantId", "AssignedDepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTasks_TenantId_AssignedUserId_Status",
                table: "SeparationClearanceTasks",
                columns: new[] { "TenantId", "AssignedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTasks_TenantId_DueDate",
                table: "SeparationClearanceTasks",
                columns: new[] { "TenantId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTasks_TenantId_SeparationClearanceId",
                table: "SeparationClearanceTasks",
                columns: new[] { "TenantId", "SeparationClearanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTasks_TenantId_Status",
                table: "SeparationClearanceTasks",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTasks_TenantId_TemplateItemId",
                table: "SeparationClearanceTasks",
                columns: new[] { "TenantId", "TemplateItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTemplateItems_TenantId_TemplateId_Code",
                table: "SeparationClearanceTemplateItems",
                columns: new[] { "TenantId", "TemplateId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTemplateItems_TenantId_TemplateId_Sequence",
                table: "SeparationClearanceTemplateItems",
                columns: new[] { "TenantId", "TemplateId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTemplates_TenantId_Code",
                table: "SeparationClearanceTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationClearanceTemplates_TenantId_EffectiveFrom_EffectiveTo",
                table: "SeparationClearanceTemplates",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeparationAssetReturns");

            migrationBuilder.DropTable(
                name: "SeparationClearanceEvents");

            migrationBuilder.DropTable(
                name: "SeparationClearanceTasks");

            migrationBuilder.DropTable(
                name: "SeparationClearanceTemplateItems");

            migrationBuilder.DropTable(
                name: "SeparationClearances");

            migrationBuilder.DropTable(
                name: "SeparationClearanceTemplates");
        }
    }
}
