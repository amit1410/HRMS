using Microsoft.Extensions.Configuration;

namespace HRMS.Infrastructure.Persistence;

internal static class DesignTimeDatabaseConfiguration
{
    internal const string SqlServer = "SqlServer";
    internal const string MySql = "MySql";

    internal static IConfiguration Load()
    {
        var current = Directory.GetCurrentDirectory();
        var apiDirectory = Path.Combine(current, "Backend", "HRMS.API");
        var basePath = File.Exists(Path.Combine(current, "appsettings.json"))
            ? current
            : File.Exists(Path.Combine(apiDirectory, "appsettings.json")) ? apiDirectory : current;
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(basePath, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(basePath, $"appsettings.{environment}.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    internal static string ResolveProvider(IConfiguration configuration, string? key = null)
    {
        var value = configuration[key ?? "Database:Provider"];
        if (key is not null && string.IsNullOrWhiteSpace(value))
            value = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(value)) return SqlServer;
        if (value.Equals(SqlServer, StringComparison.OrdinalIgnoreCase)) return SqlServer;
        if (value.Equals(MySql, StringComparison.OrdinalIgnoreCase)) return MySql;
        throw new InvalidOperationException($"DatabaseProviderNotSupported: design-time provider '{value}' is not supported. Use SqlServer or MySql.");
    }

    internal static string ResolveConnection(IConfiguration configuration, string provider, bool catalog)
    {
        var key = provider == MySql ? catalog ? "MySqlCatalog" : "MySql" : catalog ? "SqlServerCatalog" : "SqlServer";
        var configured = configuration.GetConnectionString(key);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (provider == MySql)
            throw new InvalidOperationException($"Design-time MySQL provider is selected, but ConnectionStrings:{key} is missing. Set ConnectionStrings__{key} before running dotnet ef.");
        return catalog
            ? "Server=localhost;Database=HRMS_Catalog;Trusted_Connection=True;TrustServerCertificate=True;"
            : "Server=localhost;Database=HRMS;Trusted_Connection=True;TrustServerCertificate=True;";
    }
}
