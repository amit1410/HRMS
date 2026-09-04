using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountEmployeeLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Users_TenantId_Id",
                table: "Users",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "AccountEmployeeLinkEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PreviousEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BeforeEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AfterEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmployeeLinkEvents", x => x.Id);
                    table.UniqueConstraint("AK_AccountEmployeeLinkEvents_TenantId_SubjectUserId_Id", x => new { x.TenantId, x.SubjectUserId, x.Id });
                    table.CheckConstraint("CK_AccountEmployeeLinkEvents_Shape", "[Sequence] > 0 AND [Operation] IN ('Link','Unlink','Replace') AND [Reason] <> '' AND [CorrelationId] <> '' AND (([Operation] = 'Link' AND [PreviousLinkId] IS NULL AND [BeforeEmployeeId] IS NULL AND [NewLinkId] = [Id] AND [AfterEmployeeId] IS NOT NULL) OR ([Operation] = 'Unlink' AND [PreviousLinkId] IS NOT NULL AND [BeforeEmployeeId] IS NOT NULL AND [NewLinkId] IS NULL AND [AfterEmployeeId] IS NULL) OR ([Operation] = 'Replace' AND [PreviousLinkId] IS NOT NULL AND [BeforeEmployeeId] IS NOT NULL AND [NewLinkId] = [Id] AND [AfterEmployeeId] IS NOT NULL AND [BeforeEmployeeId] <> [AfterEmployeeId]))");
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_AccountEmployeeLinkEvents_TenantId_SubjectUserId_PreviousEventId",
                        columns: x => new { x.TenantId, x.SubjectUserId, x.PreviousEventId },
                        principalTable: "AccountEmployeeLinkEvents",
                        principalColumns: new[] { "TenantId", "SubjectUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_AccountEmployeeLinkEvents_TenantId_SubjectUserId_PreviousLinkId",
                        columns: x => new { x.TenantId, x.SubjectUserId, x.PreviousLinkId },
                        principalTable: "AccountEmployeeLinkEvents",
                        principalColumns: new[] { "TenantId", "SubjectUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Employees_TenantId_AfterEmployeeId",
                        columns: x => new { x.TenantId, x.AfterEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Employees_TenantId_BeforeEmployeeId",
                        columns: x => new { x.TenantId, x.BeforeEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Users_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeLinkEvents_Users_TenantId_SubjectUserId",
                        columns: x => new { x.TenantId, x.SubjectUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountEmployeeCurrentLinks",
                columns: table => new
                {
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmployeeCurrentLinks", x => x.LinkId);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_AccountEmployeeLinkEvents_TenantId_UserId_LinkId",
                        columns: x => new { x.TenantId, x.UserId, x.LinkId },
                        principalTable: "AccountEmployeeLinkEvents",
                        principalColumns: new[] { "TenantId", "SubjectUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountEmployeeCurrentLinks_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeCurrentLinks_TenantId_UserId_LinkId",
                table: "AccountEmployeeCurrentLinks",
                columns: new[] { "TenantId", "UserId", "LinkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccountEmployeeCurrentLinks_TenantId_EmployeeId",
                table: "AccountEmployeeCurrentLinks",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccountEmployeeCurrentLinks_TenantId_UserId",
                table: "AccountEmployeeCurrentLinks",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_ActorUserId",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_AfterEmployeeId_OccurredAtUtc_Id",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "AfterEmployeeId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_BeforeEmployeeId_OccurredAtUtc_Id",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "BeforeEmployeeId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_SubjectUserId_PreviousEventId",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "SubjectUserId", "PreviousEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_SubjectUserId_PreviousLinkId",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "SubjectUserId", "PreviousLinkId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmployeeLinkEvents_TenantId_SubjectUserId_Sequence",
                table: "AccountEmployeeLinkEvents",
                columns: new[] { "TenantId", "SubjectUserId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountEmployeeCurrentLinks");

            migrationBuilder.DropTable(
                name: "AccountEmployeeLinkEvents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Users_TenantId_Id",
                table: "Users");
        }
    }
}
