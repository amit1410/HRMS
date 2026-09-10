using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveWorkingDayCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    CountryLocationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                    table.UniqueConstraint("AK_Holidays_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Holidays_Countries_CountryLocationId",
                        column: x => x.CountryLocationId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Holidays_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Holidays_WorkLocations_TenantId_WorkLocationId",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WeeklyOffConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    CountryLocationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyOffConfigurations", x => x.Id);
                    table.UniqueConstraint("AK_WeeklyOffConfigurations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_WeeklyOffConfigurations_Countries_CountryLocationId",
                        column: x => x.CountryLocationId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeeklyOffConfigurations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeeklyOffConfigurations_WorkLocations_TenantId_WorkLocationId",
                        columns: x => new { x.TenantId, x.WorkLocationId },
                        principalTable: "WorkLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WeeklyOffDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    WeeklyOffConfigurationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyOffDays", x => x.Id);
                    table.UniqueConstraint("AK_WeeklyOffDays_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_WeeklyOffDays_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeeklyOffDays_WeeklyOffConfigurations_TenantId_WeeklyOffConf~",
                        columns: x => new { x.TenantId, x.WeeklyOffConfigurationId },
                        principalTable: "WeeklyOffConfigurations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_CountryLocationId",
                table: "Holidays",
                column: "CountryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_TenantId_Date_WorkLocationId_CountryLocationId",
                table: "Holidays",
                columns: new[] { "TenantId", "Date", "WorkLocationId", "CountryLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_TenantId_WorkLocationId",
                table: "Holidays",
                columns: new[] { "TenantId", "WorkLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyOffConfigurations_CountryLocationId",
                table: "WeeklyOffConfigurations",
                column: "CountryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyOffConfigurations_TenantId_EffectiveFrom_EffectiveTo_W~",
                table: "WeeklyOffConfigurations",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo", "WorkLocationId", "CountryLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyOffConfigurations_TenantId_WorkLocationId",
                table: "WeeklyOffConfigurations",
                columns: new[] { "TenantId", "WorkLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyOffDays_TenantId_WeeklyOffConfigurationId_DayOfWeek",
                table: "WeeklyOffDays",
                columns: new[] { "TenantId", "WeeklyOffConfigurationId", "DayOfWeek" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "WeeklyOffDays");

            migrationBuilder.DropTable(
                name: "WeeklyOffConfigurations");
        }
    }
}
