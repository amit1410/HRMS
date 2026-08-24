namespace HRMS.Infrastructure.Sharding;

/// <summary>
/// Binding for the "Sharding" configuration section: how a shard key becomes a connection string, and how
/// long a host lookup is remembered.
/// <para>
/// The template is what switches the system between its two modes, and neither is a half-configured
/// accident:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Template set — one database per organization.</b> The shard key is substituted in, and a unit of
///     work with no resolved shard has nowhere to connect and fails.
///   </item>
///   <item>
///     <b>Template absent — one database for every organization</b>, isolated by the global query filters,
///     which is exactly what this system did before sharding existed. Existing deployments keep working
///     without splitting their data on the day they upgrade, and the code path is the same either way.
///   </item>
/// </list>
/// </summary>
public sealed class ShardingOptions
{
    public const string SectionName = "Sharding";

    /// <summary>The token replaced by a tenant's <c>ShardKey</c>.</summary>
    public const string ShardKeyPlaceholder = "{shardKey}";

    /// <summary>
    /// SQL Server connection-string template, e.g.
    /// <c>Server=…;Database=HRMS_Tenant_{shardKey};Trusted_Connection=True</c>. Credentials live here, in
    /// configuration, and never in the catalog database.
    /// </summary>
    public string? ConnectionStringTemplate { get; set; }

    /// <summary>The same for the SQLite development fallback, e.g. <c>Data Source=hrms-{shardKey}.db</c>.</summary>
    public string? SqliteConnectionStringTemplate { get; set; }

    /// <summary>
    /// How long a resolved host is remembered. Short on purpose: this is the window in which a suspended
    /// organization is still served, and the window in which a newly provisioned one is not yet reachable.
    /// </summary>
    public int CacheSeconds { get; set; } = 30;

    /// <summary>
    /// How long a host that resolved to nothing is remembered. Shorter still — it exists only so a flood of
    /// requests to unknown hosts cannot turn into a flood of catalog queries — but not zero, because that
    /// is precisely the traffic an attacker controls.
    /// </summary>
    public int UnknownHostCacheSeconds { get; set; } = 5;

    /// <summary>Returns the first problem found, or null when the settings are usable.</summary>
    public string? Validate()
    {
        if (CacheSeconds < 1) return $"{SectionName}:CacheSeconds must be at least 1.";
        if (UnknownHostCacheSeconds < 1) return $"{SectionName}:UnknownHostCacheSeconds must be at least 1.";

        // A template without the placeholder is the worst possible outcome of a typo: every organization
        // resolves, every organization connects, and every organization gets the same database. Nothing
        // downstream can detect it — the query filters would keep the rows apart and it would look like it
        // worked. So it is refused at startup, where a missing pair of braces is still obvious.
        foreach (var (name, template) in new[]
                 {
                     (nameof(ConnectionStringTemplate), ConnectionStringTemplate),
                     (nameof(SqliteConnectionStringTemplate), SqliteConnectionStringTemplate)
                 })
        {
            if (!string.IsNullOrWhiteSpace(template) && !template.Contains(ShardKeyPlaceholder, StringComparison.Ordinal))
            {
                return $"{SectionName}:{name} must contain the '{ShardKeyPlaceholder}' placeholder; "
                    + "without it every organization would share one database.";
            }
        }

        return null;
    }
}
