using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantHostAndShardKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Host",
                table: "Tenants",
                type: "nvarchar(253)",
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShardKey",
                table: "Tenants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Hand-written, and it has to run here — between the columns and their unique indexes. Both
            // columns are NOT NULL, so every existing row is created holding the empty-string default; a
            // second tenant would then collide with the first the moment the indexes are created.
            //
            // The values are derived from TenantCode, which is already unique, so the backfill cannot
            // collide either. The '.localhost' suffix is deliberately not a real domain: no production
            // request will ever carry it, so an upgraded tenant is unreachable by host until an operator
            // sets its actual domain. That is the safe direction to fail — guessing a domain would route
            // real traffic somewhere on the strength of a migration's assumption.
            migrationBuilder.Sql(
                """
                UPDATE Tenants
                SET Host = LOWER(TenantCode) + '.localhost',
                    ShardKey = LOWER(TenantCode)
                WHERE Host = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Host",
                table: "Tenants",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ShardKey",
                table: "Tenants",
                column: "ShardKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_Host",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ShardKey",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Host",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ShardKey",
                table: "Tenants");
        }
    }
}
