using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeSalaryAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalaryStructureComponents_TenantId_Id",
                table: "SalaryStructureComponents",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "EmployeeSalaryAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryStructureId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryStructureVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    AnnualCtc = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    MonthlyCtc = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    PayFrequency = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChangeReason = table.Column<int>(type: "int", nullable: false),
                    Remarks = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSalaryAssignments", x => x.Id);
                    table.UniqueConstraint("AK_EmployeeSalaryAssignments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryAssignments_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryAssignments_SalaryStructureVersions_TenantId_S~",
                        columns: x => new { x.TenantId, x.SalaryStructureVersionId },
                        principalTable: "SalaryStructureVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryAssignments_SalaryStructures_TenantId_SalarySt~",
                        columns: x => new { x.TenantId, x.SalaryStructureId },
                        principalTable: "SalaryStructures",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryAssignments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeSalaryAssignmentHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeSalaryAssignmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryStructureId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryStructureVersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    AnnualCtc = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    MonthlyCtc = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    PayFrequency = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChangeReason = table.Column<int>(type: "int", nullable: false),
                    Remarks = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    ComponentsJson = table.Column<string>(type: "longtext", maxLength: 20000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSalaryAssignmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryAssignmentHistories_EmployeeSalaryAssignments_~",
                        columns: x => new { x.TenantId, x.EmployeeSalaryAssignmentId },
                        principalTable: "EmployeeSalaryAssignments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryAssignmentHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryAssignmentHistories_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeSalaryComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EmployeeSalaryAssignmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryStructureComponentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SalaryComponentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OverrideValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    OverridePercentage = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    OverrideFormula = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Remarks = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSalaryComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryComponents_EmployeeSalaryAssignments_TenantId_~",
                        columns: x => new { x.TenantId, x.EmployeeSalaryAssignmentId },
                        principalTable: "EmployeeSalaryAssignments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryComponents_SalaryComponents_TenantId_SalaryCom~",
                        columns: x => new { x.TenantId, x.SalaryComponentId },
                        principalTable: "SalaryComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryComponents_SalaryStructureComponents_TenantId_~",
                        columns: x => new { x.TenantId, x.SalaryStructureComponentId },
                        principalTable: "SalaryStructureComponents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryComponents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryAssignmentHistories_TenantId_ActorUserId",
                table: "EmployeeSalaryAssignmentHistories",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryAssignmentHistories_TenantId_EmployeeSalaryAss~",
                table: "EmployeeSalaryAssignmentHistories",
                columns: new[] { "TenantId", "EmployeeSalaryAssignmentId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryAssignments_TenantId_EmployeeId_EffectiveFrom",
                table: "EmployeeSalaryAssignments",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryAssignments_TenantId_EmployeeId_Status_Effecti~",
                table: "EmployeeSalaryAssignments",
                columns: new[] { "TenantId", "EmployeeId", "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryAssignments_TenantId_SalaryStructureId",
                table: "EmployeeSalaryAssignments",
                columns: new[] { "TenantId", "SalaryStructureId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryAssignments_TenantId_SalaryStructureVersionId",
                table: "EmployeeSalaryAssignments",
                columns: new[] { "TenantId", "SalaryStructureVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryComponents_TenantId_EmployeeSalaryAssignmentI~1",
                table: "EmployeeSalaryComponents",
                columns: new[] { "TenantId", "EmployeeSalaryAssignmentId", "SalaryStructureComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryComponents_TenantId_EmployeeSalaryAssignmentId~",
                table: "EmployeeSalaryComponents",
                columns: new[] { "TenantId", "EmployeeSalaryAssignmentId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryComponents_TenantId_SalaryComponentId",
                table: "EmployeeSalaryComponents",
                columns: new[] { "TenantId", "SalaryComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryComponents_TenantId_SalaryStructureComponentId",
                table: "EmployeeSalaryComponents",
                columns: new[] { "TenantId", "SalaryStructureComponentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeSalaryAssignmentHistories");

            migrationBuilder.DropTable(
                name: "EmployeeSalaryComponents");

            migrationBuilder.DropTable(
                name: "EmployeeSalaryAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalaryStructureComponents_TenantId_Id",
                table: "SalaryStructureComponents");
        }
    }
}
