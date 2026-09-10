using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CountryLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "WeeklyOffConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CountryLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "WeeklyOffDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeeklyOffConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                        name: "FK_WeeklyOffDays_WeeklyOffConfigurations_TenantId_WeeklyOffConfigurationId",
                        columns: x => new { x.TenantId, x.WeeklyOffConfigurationId },
                        principalTable: "WeeklyOffConfigurations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_WeeklyOffConfigurations_TenantId_EffectiveFrom_EffectiveTo_WorkLocationId_CountryLocationId",
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
