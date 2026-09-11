using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceShiftRosterFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeRosterChangeHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RosterDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousDayType = table.Column<int>(type: "int", nullable: false),
                    NewDayType = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UploadBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeRosterChangeHistories", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeRosterChangeHistories_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeRosterChangeHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterUploadBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ValidRows = table.Column<int>(type: "int", nullable: false),
                    InvalidRows = table.Column<int>(type: "int", nullable: false),
                    CommittedRows = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterUploadBatches", x => x.Id);
                    table.UniqueConstraint("AK_RosterUploadBatches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_RosterUploadBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftPatterns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CycleLengthDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftPatterns", x => x.Id);
                    table.UniqueConstraint("AK_ShiftPatterns_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ShiftPatterns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ShiftName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    BreakDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    MinimumWorkMinutes = table.Column<int>(type: "int", nullable: false),
                    FullDayWorkMinutes = table.Column<int>(type: "int", nullable: false),
                    HalfDayWorkMinutes = table.Column<int>(type: "int", nullable: true),
                    GraceInMinutes = table.Column<int>(type: "int", nullable: false),
                    GraceOutMinutes = table.Column<int>(type: "int", nullable: false),
                    LateThresholdMinutes = table.Column<int>(type: "int", nullable: false),
                    EarlyOutThresholdMinutes = table.Column<int>(type: "int", nullable: false),
                    IsNightShift = table.Column<bool>(type: "bit", nullable: false),
                    CrossesMidnight = table.Column<bool>(type: "bit", nullable: false),
                    CaptureMode = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                    table.UniqueConstraint("AK_Shifts_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Shifts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterUploadRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RosterUploadBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RosterDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DayType = table.Column<int>(type: "int", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterUploadRows", x => x.Id);
                    table.UniqueConstraint("AK_RosterUploadRows_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_RosterUploadRows_RosterUploadBatches_TenantId_RosterUploadBatchId",
                        columns: x => new { x.TenantId, x.RosterUploadBatchId },
                        principalTable: "RosterUploadBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterUploadRows_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeRosterDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RosterDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShiftPatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DayType = table.Column<int>(type: "int", nullable: false),
                    AssignmentSource = table.Column<int>(type: "int", nullable: false),
                    IsOverride = table.Column<bool>(type: "bit", nullable: false),
                    IsCalendarOverride = table.Column<bool>(type: "bit", nullable: false),
                    OriginalShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeEmploymentHistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeRosterDays", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeRosterDays_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeRosterDays_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeRosterDays_ShiftPatterns_TenantId_ShiftPatternId",
                        columns: x => new { x.TenantId, x.ShiftPatternId },
                        principalTable: "ShiftPatterns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeRosterDays_Shifts_TenantId_ShiftId",
                        columns: x => new { x.TenantId, x.ShiftId },
                        principalTable: "Shifts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftApplicabilityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShiftPatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: true),
                    HoldingCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FunctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubFunctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DesignationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CountryLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftApplicabilityRules", x => x.Id);
                    table.UniqueConstraint("AK_ShiftApplicabilityRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ShiftApplicabilityRules_ShiftPatterns_TenantId_ShiftPatternId",
                        columns: x => new { x.TenantId, x.ShiftPatternId },
                        principalTable: "ShiftPatterns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftApplicabilityRules_Shifts_TenantId_ShiftId",
                        columns: x => new { x.TenantId, x.ShiftId },
                        principalTable: "Shifts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftApplicabilityRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftPatternDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftPatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceDay = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DayType = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftPatternDays", x => x.Id);
                    table.UniqueConstraint("AK_ShiftPatternDays_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ShiftPatternDays_ShiftPatterns_TenantId_ShiftPatternId",
                        columns: x => new { x.TenantId, x.ShiftPatternId },
                        principalTable: "ShiftPatterns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftPatternDays_Shifts_TenantId_ShiftId",
                        columns: x => new { x.TenantId, x.ShiftId },
                        principalTable: "Shifts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRosterChangeHistories_TenantId_EmployeeId_RosterDate",
                table: "EmployeeRosterChangeHistories",
                columns: new[] { "TenantId", "EmployeeId", "RosterDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRosterDays_TenantId_EmployeeId_RosterDate",
                table: "EmployeeRosterDays",
                columns: new[] { "TenantId", "EmployeeId", "RosterDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRosterDays_TenantId_RosterDate",
                table: "EmployeeRosterDays",
                columns: new[] { "TenantId", "RosterDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRosterDays_TenantId_ShiftId",
                table: "EmployeeRosterDays",
                columns: new[] { "TenantId", "ShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRosterDays_TenantId_ShiftPatternId",
                table: "EmployeeRosterDays",
                columns: new[] { "TenantId", "ShiftPatternId" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterUploadBatches_TenantId_CreatedDate",
                table: "RosterUploadBatches",
                columns: new[] { "TenantId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterUploadRows_TenantId_RosterUploadBatchId_RowNumber",
                table: "RosterUploadRows",
                columns: new[] { "TenantId", "RosterUploadBatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftApplicabilityRules_TenantId_Priority_EffectiveFrom",
                table: "ShiftApplicabilityRules",
                columns: new[] { "TenantId", "Priority", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftApplicabilityRules_TenantId_ShiftId",
                table: "ShiftApplicabilityRules",
                columns: new[] { "TenantId", "ShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftApplicabilityRules_TenantId_ShiftPatternId",
                table: "ShiftApplicabilityRules",
                columns: new[] { "TenantId", "ShiftPatternId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPatternDays_TenantId_ShiftId",
                table: "ShiftPatternDays",
                columns: new[] { "TenantId", "ShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPatternDays_TenantId_ShiftPatternId_SequenceDay",
                table: "ShiftPatternDays",
                columns: new[] { "TenantId", "ShiftPatternId", "SequenceDay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPatterns_TenantId_Code",
                table: "ShiftPatterns",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_TenantId_EffectiveFrom_EffectiveTo",
                table: "Shifts",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_TenantId_ShiftCode",
                table: "Shifts",
                columns: new[] { "TenantId", "ShiftCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeRosterChangeHistories");

            migrationBuilder.DropTable(
                name: "EmployeeRosterDays");

            migrationBuilder.DropTable(
                name: "RosterUploadRows");

            migrationBuilder.DropTable(
                name: "ShiftApplicabilityRules");

            migrationBuilder.DropTable(
                name: "ShiftPatternDays");

            migrationBuilder.DropTable(
                name: "RosterUploadBatches");

            migrationBuilder.DropTable(
                name: "ShiftPatterns");

            migrationBuilder.DropTable(
                name: "Shifts");

        }
    }
}
