using Microsoft.Extensions.Configuration;

namespace HRMS.Infrastructure.Persistence;

/// <summary>
/// Reads the <c>Database:Provider</c> setting. One place, because the catalog context, the tenant context
/// and the shard connection-string factory all have to reach the same answer — a disagreement would produce
/// a SQL Server catalog pointing at SQLite tenant databases, or the reverse.
/// </summary>
internal static class ConfiguredProvider
{
    private const string SqliteProviderName = "Sqlite";

    internal enum CatalogProviderKind
    {
        SqlServer,
        MySql,
        Sqlite
    }

    /// <summary>True when configuration selects the SQLite development fallback. SQL Server is the default.</summary>
    internal static bool IsSqlite(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"] ?? "SqlServer", SqliteProviderName, StringComparison.OrdinalIgnoreCase);

    internal static CatalogProviderKind ResolveCatalogProvider(IConfiguration configuration)
    {
        var explicitProvider = configuration["Database:CatalogProvider"];
        if (explicitProvider is not null)
        {
            if (string.Equals(explicitProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
                return CatalogProviderKind.SqlServer;
            if (string.Equals(explicitProvider, "MySql", StringComparison.OrdinalIgnoreCase))
                return CatalogProviderKind.MySql;
            if (string.Equals(explicitProvider, SqliteProviderName, StringComparison.OrdinalIgnoreCase))
                return CatalogProviderKind.Sqlite;

            throw new InvalidOperationException(
                $"DatabaseProviderNotSupported: catalog provider '{explicitProvider}' is not supported.");
        }

        return IsSqlite(configuration) ? CatalogProviderKind.Sqlite : CatalogProviderKind.SqlServer;
    }
}
