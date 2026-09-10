using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    [DbContext(typeof(HrmsDbContext))]
    [Migration("20260910134300_AddLeavePeriodCloseOccurrencesV2")]
    partial class AddLeavePeriodCloseOccurrencesV2
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }
}
