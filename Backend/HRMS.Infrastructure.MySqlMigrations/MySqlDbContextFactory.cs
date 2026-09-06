using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HRMS.Infrastructure.MySqlMigrations;

public sealed class MySqlDbContextFactory : IDesignTimeDbContextFactory<HrmsDbContext>
{
    public HrmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL(
                "Server=localhost;Database=HRMS_MigrationDesign;",
                mysql => mysql.MigrationsAssembly(typeof(MySqlDbContextFactory).Assembly.FullName))
            .Options;

        return new HrmsDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
        public bool HasTenant => false;
    }
}
