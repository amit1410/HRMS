using System.Data.Common;
using Microsoft.Extensions.Configuration;

namespace HRMS.Infrastructure.Persistence;

/// <summary>Resolves safe catalog connection metadata without creating a DbContext or opening a connection.</summary>
public static class CatalogConnectionResolver
{
    public static CatalogConnectionDetails Resolve(IConfiguration configuration)
    {
        var provider = ConfiguredProvider.ResolveCatalogProvider(configuration);
        var connectionKey = provider is ConfiguredProvider.CatalogProviderKind.Sqlite
            ? "ConnectionStrings:SqliteCatalog"
            : "ConnectionStrings:Catalog";
        var connectionString = ConfiguredProvider.ResolveCatalogConnectionString(configuration, provider);

        var values = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var host = provider is ConfiguredProvider.CatalogProviderKind.Sqlite
            ? null
            : Value(values, "Server", "Host", "Data Source", "Address", "Network Address");
        var port = ParsePort(Value(values, "Port")) ?? ParseServerPort(host, out host);
        var database = Value(values, "Database", "Initial Catalog");
        if (provider is ConfiguredProvider.CatalogProviderKind.Sqlite)
            database ??= Value(values, "Data Source");

        var providerName = provider switch
        {
            ConfiguredProvider.CatalogProviderKind.SqlServer => "SqlServer",
            ConfiguredProvider.CatalogProviderKind.MySql => "MySql",
            ConfiguredProvider.CatalogProviderKind.Sqlite => "Sqlite",
            _ => throw new InvalidOperationException("The configured catalog provider is not supported.")
        };

        var providerResolution = configuration["Database:CatalogProvider"] is null
            ? "Database:Provider fallback"
            : "Database:CatalogProvider";

        return new CatalogConnectionDetails(
            providerName,
            host,
            port,
            database,
            $"{providerResolution}; {connectionKey}");
    }

    private static string? Value(DbConnectionStringBuilder values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && value is not null)
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static int? ParsePort(string? value) =>
        int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : null;

    private static int? ParseServerPort(string? server, out string? host)
    {
        host = server;
        if (string.IsNullOrWhiteSpace(server)) return null;
        var comma = server.LastIndexOf(',');
        if (comma <= 0 || !int.TryParse(server[(comma + 1)..], out var port) || port is < 1 or > 65535)
            return null;
        host = server[..comma];
        return port;
    }
}

public sealed record CatalogConnectionDetails(
    string Provider,
    string? Host,
    int? Port,
    string? Database,
    string ConfigurationResolution);
