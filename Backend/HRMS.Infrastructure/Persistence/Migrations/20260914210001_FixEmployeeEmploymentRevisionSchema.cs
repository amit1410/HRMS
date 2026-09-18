using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

#nullable disable

namespace HRMS.Infrastructure.Persistence.Migrations;

/// <summary>Provider-equivalent repair for the employment revision schema.</summary>
[Migration("20260914210001_FixEmployeeEmploymentRevisionSchema")]
[DbContext(typeof(HrmsDbContext))]
public partial class FixEmployeeEmploymentRevisionSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The preceding AddEmploymentHistoryRevisions migration already creates
        // this schema. Keep this migration in history for databases that have
        // recorded it, but do not recreate the same columns and index.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The schema is owned by AddEmploymentHistoryRevisions, so rolling back
        // this compatibility migration must not remove those objects.
    }
}
