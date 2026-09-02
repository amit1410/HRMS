using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCodeRuleConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeCodeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCodeConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeRules", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeCodeRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRules_EmployeeCodeConfigs_TenantId_EmployeeCodeConfigId",
                        columns: x => new { x.TenantId, x.EmployeeCodeConfigId },
                        principalTable: "EmployeeCodeConfigs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeCodeRuleConditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCodeRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Field = table.Column<int>(type: "int", nullable: false),
                    Operator = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeRuleConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRuleConditions_EmployeeCodeRules_TenantId_EmployeeCodeRuleId",
                        columns: x => new { x.TenantId, x.EmployeeCodeRuleId },
                        principalTable: "EmployeeCodeRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeRuleConditions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeCodeSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCodeRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceOrder = table.Column<int>(type: "int", nullable: false),
                    SegmentType = table.Column<int>(type: "int", nullable: false),
                    FixedValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaddingLength = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSegments_EmployeeCodeRules_TenantId_EmployeeCodeRuleId",
                        columns: x => new { x.TenantId, x.EmployeeCodeRuleId },
                        principalTable: "EmployeeCodeRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSegments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeCodeSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCodeRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    IncrementBy = table.Column<int>(type: "int", nullable: false),
                    ResetPeriod = table.Column<int>(type: "int", nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSequences_EmployeeCodeRules_TenantId_EmployeeCodeRuleId",
                        columns: x => new { x.TenantId, x.EmployeeCodeRuleId },
                        principalTable: "EmployeeCodeRules",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeSequences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeRuleConditions_TenantId_EmployeeCodeRuleId",
                table: "EmployeeCodeRuleConditions",
                columns: new[] { "TenantId", "EmployeeCodeRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeRules_TenantId_EmployeeCodeConfigId_Priority",
                table: "EmployeeCodeRules",
                columns: new[] { "TenantId", "EmployeeCodeConfigId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeSegments_TenantId_EmployeeCodeRuleId_SequenceOrder",
                table: "EmployeeCodeSegments",
                columns: new[] { "TenantId", "EmployeeCodeRuleId", "SequenceOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeSequences_TenantId_EmployeeCodeRuleId_Scope_ScopeKey_PeriodKey",
                table: "EmployeeCodeSequences",
                columns: new[] { "TenantId", "EmployeeCodeRuleId", "Scope", "ScopeKey", "PeriodKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeCodeRuleConditions");

            migrationBuilder.DropTable(
                name: "EmployeeCodeSegments");

            migrationBuilder.DropTable(
                name: "EmployeeCodeSequences");

            migrationBuilder.DropTable(
                name: "EmployeeCodeRules");
        }
    }
}
