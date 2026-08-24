using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.Sharding;

/// <summary>Turns a resolved shard into the connection string for that organization's database.</summary>
internal interface IShardConnectionStringFactory
{
    /// <summary>
    /// The connection string for <paramref name="shard"/>.
    /// <para>
    /// With a template configured, a null <paramref name="shard"/> throws: there is no default database to
    /// fall back to, and inventing one is the failure where one organization's rows are written into
    /// another's database. Without a template, every caller gets the single shared database — the behaviour
    /// this system had before sharding.
    /// </para>
    /// </summary>
    string For(ShardDescriptor? shard);
}

/// <inheritdoc cref="IShardConnectionStringFactory"/>
internal sealed class ShardConnectionStringFactory : IShardConnectionStringFactory
{
    private readonly IConfiguration _configuration;
    private readonly string? _template;
    private readonly bool _sqlite;

    public ShardConnectionStringFactory(
        IConfiguration configuration,
        IOptions<ShardingOptions> options,
        ILogger<ShardConnectionStringFactory> logger)
    {
        _configuration = configuration;
        _sqlite = ConfiguredProvider.IsSqlite(configuration);

        var sharding = options.Value;
        _template = _sqlite ? sharding.SqliteConnectionStringTemplate : sharding.ConnectionStringTemplate;

        if (string.IsNullOrWhiteSpace(_template))
        {
            _template = null;

            // Worth saying out loud at startup. It is a supported mode, not a misconfiguration, but which of
            // the two modes a deployment is in changes what "tenant isolation" is resting on — separate
            // databases, or the query filters alone.
            var setting = _sqlite
                ? nameof(ShardingOptions.SqliteConnectionStringTemplate)
                : nameof(ShardingOptions.ConnectionStringTemplate);

            logger.LogWarning(
                "No '{Setting}' connection-string template is configured, so every organization shares one "
                + "database and tenant isolation rests on the global query filters. Set it to give each "
                + "organization its own database.",
                $"{ShardingOptions.SectionName}:{setting}");
        }
    }

    public string For(ShardDescriptor? shard)
    {
        if (_template is null)
        {
            return SharedConnectionString();
        }

        if (shard is null)
        {
            throw new InvalidOperationException(
                "No organization has been resolved for this scope, so there is no database to open. During a "
                + "request the host-resolution middleware selects one; outside a request (startup, "
                + "provisioning, seeding, design-time tooling) the caller must select one itself with "
                + $"{nameof(IShardContext)}.{nameof(IShardContext.Use)} before resolving a tenant DbContext.");
        }

        // The shard key comes out of the catalog and goes into a connection string, which makes this a
        // template-injection sink: a key containing ';' could append 'Password=…' on SQL Server or repoint
        // 'Data Source=' on SQLite, turning one admin-entered field into a way to choose a database and
        // credentials. Provisioning validates on the way in; this validates on the way out, because a
        // connection string assembled from database content should never trust the content.
        if (!IsSafeShardKey(shard.ShardKey))
        {
            throw new InvalidOperationException(
                $"Organization '{shard.TenantCode}' has a shard key that is not a safe database name. It must "
                + $"be 1-{TenantMapping.ShardKeyMaxLength} characters of lowercase letters, digits, '-' or "
                + "'_', and start with a letter or digit.");
        }

        return _template.Replace(ShardingOptions.ShardKeyPlaceholder, shard.ShardKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// The single-database connection string. Resolved on use rather than in the constructor so a sharded
    /// deployment is not required to configure a shared database it will never open.
    /// </summary>
    private string SharedConnectionString()
    {
        if (_sqlite)
        {
            return _configuration.GetConnectionString("Sqlite") ?? "Data Source=hrms_dev.db";
        }

        return _configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("Connection string 'SqlServer' is not configured.");
    }

    private static bool IsSafeShardKey(string shardKey)
    {
        if (shardKey.Length is 0 or > TenantMapping.ShardKeyMaxLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(shardKey[0]) && !char.IsAsciiDigit(shardKey[0]))
        {
            return false;
        }

        foreach (var character in shardKey)
        {
            var allowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '-' or '_';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
