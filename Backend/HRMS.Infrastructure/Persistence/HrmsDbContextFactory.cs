using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HRMS.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tools (`dotnet ef migrations add`, `database update`).
/// It always targets SQL Server so the generated migrations contain SQL Server DDL — the mandated
/// production database — regardless of which provider the running app is configured to use.
/// It does not open a connection at migration-scaffolding time.
/// </summary>
public class HrmsDbContextFactory : IDesignTimeDbContextFactory<HrmsDbContext>
{
    public HrmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=HRMS;Trusted_Connection=True;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName))
            .Options;

        return new HrmsDbContext(options, new DesignTimeTenantContext());
    }

    /// <summary>No tenant is resolved at design time; migrations only need the model shape.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
        public bool HasTenant => false;
    }
}
