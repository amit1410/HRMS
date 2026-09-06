using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.Sharding;

internal interface IShardConnectionStringFactory
{
    string For(ShardDescriptor? shard);
}

internal sealed class ShardConnectionStringFactory : IShardConnectionStringFactory
{
    private readonly IConfiguration _configuration;
    private readonly string? _legacySqlServerTemplate;
    private readonly string? _sqlServerTemplate;
    private readonly string? _mySqlTemplate;
    private readonly string? _sqliteTemplate;
    private readonly bool _sqlite;

    public ShardConnectionStringFactory(
        IConfiguration configuration,
        IOptions<ShardingOptions> options,
        ILogger<ShardConnectionStringFactory> logger)
    {
        _configuration = configuration;
        _sqlite = ConfiguredProvider.IsSqlite(configuration);

        var sharding = options.Value;
        _legacySqlServerTemplate = NullIfWhiteSpace(sharding.ConnectionStringTemplate);
        _sqlServerTemplate = NullIfWhiteSpace(sharding.SqlServerConnectionStringTemplate);
        _mySqlTemplate = NullIfWhiteSpace(sharding.MySqlConnectionStringTemplate);
        _sqliteTemplate = NullIfWhiteSpace(sharding.SqliteConnectionStringTemplate);

        if (_sqlite)
        {
            LogSharedFallbackIfNeeded(_sqliteTemplate, nameof(ShardingOptions.SqliteConnectionStringTemplate), logger);
        }
        else if (_sqlServerTemplate is null && _legacySqlServerTemplate is null)
        {
            LogSharedFallbackIfNeeded(null, nameof(ShardingOptions.SqlServerConnectionStringTemplate), logger);
        }
    }

    public string For(ShardDescriptor? shard)
    {
        if (shard is null)
        {
            if (_sqliteTemplate is null && _sqlServerTemplate is null && _legacySqlServerTemplate is null)
                return SharedConnectionString(DatabaseProviderType.SqlServer);

            throw NoShardSelected();
        }

        var template = _sqlite
            ? _sqliteTemplate
            : shard.DatabaseProvider switch
            {
                DatabaseProviderType.SqlServer => _sqlServerTemplate ?? _legacySqlServerTemplate,
                DatabaseProviderType.MySql => _mySqlTemplate ?? throw MissingTemplate(DatabaseProviderType.MySql),
                _ => throw new InvalidOperationException(
                    $"DatabaseProviderNotSupported: tenant provider '{shard.DatabaseProvider}' is not supported.")
            };

        if (template is null)
            return SharedConnectionString(shard.DatabaseProvider);

        if (!IsSafeShardKey(shard.ShardKey))
        {
            throw new InvalidOperationException(
                $"Organization '{shard.TenantCode}' has a shard key that is not a safe database name. It must "
                + $"be 1-{TenantMapping.ShardKeyMaxLength} characters of lowercase letters, digits, '-' or "
                + "'_', and start with a letter or digit.");
        }

        return template.Replace(ShardingOptions.ShardKeyPlaceholder, shard.ShardKey, StringComparison.Ordinal);
    }

    private string SharedConnectionString(DatabaseProviderType provider)
    {
        if (_sqlite)
            return _configuration.GetConnectionString("Sqlite") ?? "Data Source=hrms_dev.db";

        if (provider is DatabaseProviderType.MySql)
            throw MissingTemplate(provider);

        return _configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("Connection string 'SqlServer' is not configured.");
    }

    private static InvalidOperationException NoShardSelected() => new(
        "No organization has been resolved for this scope, so there is no tenant database to open. "
        + $"During a request the host-resolution middleware selects one; outside a request use {nameof(IShardContext)}.{nameof(IShardContext.Use)}.");

    private static InvalidOperationException MissingTemplate(DatabaseProviderType provider) => new(
        $"Sharding:{(provider is DatabaseProviderType.MySql ? nameof(ShardingOptions.MySqlConnectionStringTemplate) : nameof(ShardingOptions.SqlServerConnectionStringTemplate))} "
        + $"is required for tenant provider '{provider}'.");

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void LogSharedFallbackIfNeeded(string? template, string setting, ILogger logger)
    {
        if (template is null)
        {
            logger.LogWarning(
                "No '{Setting}' connection-string template is configured, so organizations use the configured shared database fallback.",
                $"{ShardingOptions.SectionName}:{setting}");
        }
    }

    private static bool IsSafeShardKey(string shardKey)
    {
        if (shardKey.Length is 0 or > TenantMapping.ShardKeyMaxLength)
            return false;

        if (!char.IsAsciiLetterLower(shardKey[0]) && !char.IsAsciiDigit(shardKey[0]))
            return false;

        foreach (var character in shardKey)
        {
            var allowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '-' or '_';

            if (!allowed)
                return false;
        }

        return true;
    }
}
