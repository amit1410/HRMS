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

    /// <summary>True when configuration selects the SQLite development fallback. SQL Server is the default.</summary>
    internal static bool IsSqlite(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"] ?? "SqlServer", SqliteProviderName, StringComparison.OrdinalIgnoreCase);
}
