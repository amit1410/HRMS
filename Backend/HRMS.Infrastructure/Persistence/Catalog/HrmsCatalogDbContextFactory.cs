using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HRMS.Infrastructure.Persistence.Catalog;

/// <summary>
/// Design-time factory for the catalog context, used by the EF Core tools. Targets SQL Server for the
/// same reason <see cref="HrmsDbContextFactory"/> does: the generated migrations must contain SQL Server
/// DDL, which is the production database, whatever provider the running app happens to be configured for.
/// It does not open a connection at scaffolding time.
/// <para>
/// The catalog keeps its own migration chain, separate from the shard chain. They describe different
/// databases with different lifecycles — one catalog is created once, a shard is created per customer — so
/// a single chain with one history table could never be applied correctly to either.
/// </para>
/// <para>
/// The history table is named here as well as at runtime, and the two must agree: <c>dotnet ef database
/// update</c> comes through this factory, so a default here and a named table there would have the tools
/// recording migrations in one place while the application looked for them in another.
/// </para>
/// </summary>
public class HrmsCatalogDbContextFactory : IDesignTimeDbContextFactory<HrmsCatalogDbContext>
{
    public HrmsCatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=HRMS_Catalog;Trusted_Connection=True;TrustServerCertificate=True;",
                sql =>
                {
                    sql.MigrationsAssembly(typeof(HrmsCatalogDbContext).Assembly.FullName);
                    sql.MigrationsHistoryTable(DependencyInjection.CatalogHistoryTable);
                })
            .Options;

        return new HrmsCatalogDbContext(options);
    }
}
