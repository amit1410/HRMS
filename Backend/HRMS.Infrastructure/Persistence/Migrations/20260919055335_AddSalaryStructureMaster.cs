using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryStructureMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalaryStructures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryStructures", x => x.Id);
                    table.UniqueConstraint("AK_SalaryStructures_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SalaryStructures_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryStructureVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalaryStructureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryStructureVersions", x => x.Id);
                    table.UniqueConstraint("AK_SalaryStructureVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SalaryStructureVersions_SalaryStructures_TenantId_SalaryStructureId",
                        columns: x => new { x.TenantId, x.SalaryStructureId },
                        principalTable: "SalaryStructures",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryStructureComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalaryStructureVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalaryComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CalculationType = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    PercentageOfComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Formula = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsProratable = table.Column<bool>(type: "bit", nullable: false),
                    IsEditableAtEmployeeLevel = table.Column<bool>(type: "bit", nullable: false),
                    MinimumAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    MaximumAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryStructureComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryStructureComponents_PercentageBase",
                        columns: x => new { x.TenantId, x.PercentageOfComponentId },
                        principalTable: "SalaryComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureComponents_SalaryComponent",
                        columns: x => new { x.TenantId, x.SalaryComponentId },
                        principalTable: "SalaryComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureComponents_SalaryStructureVersions_TenantId_SalaryStructureVersionId",
                        columns: x => new { x.TenantId, x.SalaryStructureVersionId },
                        principalTable: "SalaryStructureVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalaryStructureComponents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryStructureHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalaryStructureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalaryStructureVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ComponentsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryStructureHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryStructureHistories_SalaryStructureVersions_TenantId_SalaryStructureVersionId",
                        columns: x => new { x.TenantId, x.SalaryStructureVersionId },
                        principalTable: "SalaryStructureVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureHistories_SalaryStructures_TenantId_SalaryStructureId",
                        columns: x => new { x.TenantId, x.SalaryStructureId },
                        principalTable: "SalaryStructures",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureHistories_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureComponents_TenantId_PercentageOfComponentId",
                table: "SalaryStructureComponents",
                columns: new[] { "TenantId", "PercentageOfComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureComponents_TenantId_SalaryComponentId",
                table: "SalaryStructureComponents",
                columns: new[] { "TenantId", "SalaryComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureComponents_TenantId_SalaryStructureVersionId_SalaryComponentId",
                table: "SalaryStructureComponents",
                columns: new[] { "TenantId", "SalaryStructureVersionId", "SalaryComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureComponents_TenantId_SalaryStructureVersionId_Sequence",
                table: "SalaryStructureComponents",
                columns: new[] { "TenantId", "SalaryStructureVersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureHistories_TenantId_ActorUserId",
                table: "SalaryStructureHistories",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureHistories_TenantId_SalaryStructureId_ChangedAtUtc",
                table: "SalaryStructureHistories",
                columns: new[] { "TenantId", "SalaryStructureId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureHistories_TenantId_SalaryStructureVersionId",
                table: "SalaryStructureHistories",
                columns: new[] { "TenantId", "SalaryStructureVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructures_TenantId_Code",
                table: "SalaryStructures",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructures_TenantId_IsActive",
                table: "SalaryStructures",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureVersions_TenantId_EffectiveFrom_EffectiveTo",
                table: "SalaryStructureVersions",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureVersions_TenantId_SalaryStructureId_EffectiveFrom",
                table: "SalaryStructureVersions",
                columns: new[] { "TenantId", "SalaryStructureId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalaryStructureComponents");

            migrationBuilder.DropTable(
                name: "SalaryStructureHistories");

            migrationBuilder.DropTable(
                name: "SalaryStructureVersions");

            migrationBuilder.DropTable(
                name: "SalaryStructures");
        }
    }
}
