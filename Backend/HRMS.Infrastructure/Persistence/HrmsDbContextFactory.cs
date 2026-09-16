using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MySql.EntityFrameworkCore.Extensions;

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
        var configuration = DesignTimeDatabaseConfiguration.Load();
        var provider = DesignTimeDatabaseConfiguration.ResolveProvider(configuration);
        var connection = DesignTimeDatabaseConfiguration.ResolveConnection(configuration, provider, false);
        var optionsBuilder = new DbContextOptionsBuilder<HrmsDbContext>();
        if (provider == DesignTimeDatabaseConfiguration.MySql)
            optionsBuilder.UseMySQL(connection, mysql => mysql.MigrationsAssembly(DatabaseProviderNames.MySqlMigrationsAssembly));
        else
            optionsBuilder.UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName));

        return new HrmsDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }

    /// <summary>No tenant is resolved at design time; migrations only need the model shape.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
        public bool HasTenant => false;
    }
}
