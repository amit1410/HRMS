using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCodeConfigVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeCodeConfigVersionId",
                table: "EmployeeCodeRules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeCodeConfigVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCodeConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutoGenerate = table.Column<bool>(type: "bit", nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Separator = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Padding = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCodeConfigVersions", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeCodeConfigVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeCodeConfigVersions_EmployeeCodeConfigs_TenantId_EmployeeCodeConfigId",
                        columns: x => new { x.TenantId, x.EmployeeCodeConfigId },
                        principalTable: "EmployeeCodeConfigs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeCodeConfigVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeRules_TenantId_EmployeeCodeConfigVersionId",
                table: "EmployeeCodeRules",
                columns: new[] { "TenantId", "EmployeeCodeConfigVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCodeConfigVersions_TenantId_EmployeeCodeConfigId_EffectiveFrom",
                table: "EmployeeCodeConfigVersions",
                columns: new[] { "TenantId", "EmployeeCodeConfigId", "EffectiveFrom" });

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeCodeRules_EmployeeCodeConfigVersions_TenantId_EmployeeCodeConfigVersionId",
                table: "EmployeeCodeRules",
                columns: new[] { "TenantId", "EmployeeCodeConfigVersionId" },
                principalTable: "EmployeeCodeConfigVersions",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeCodeRules_EmployeeCodeConfigVersions_TenantId_EmployeeCodeConfigVersionId",
                table: "EmployeeCodeRules");

            migrationBuilder.DropTable(
                name: "EmployeeCodeConfigVersions");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeCodeRules_TenantId_EmployeeCodeConfigVersionId",
                table: "EmployeeCodeRules");

            migrationBuilder.DropColumn(
                name: "EmployeeCodeConfigVersionId",
                table: "EmployeeCodeRules");
        }
    }
}
