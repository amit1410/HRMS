namespace HRMS.Tests.TestSupport;

/// <summary>Non-secret configuration values for a later isolated browser run.</summary>
public sealed record BrowserAcceptanceConfiguration(
    int ApiPort,
    int FrontendPort,
    string ApiOrigin,
    string FrontendOrigin,
    IReadOnlyList<string> WorkspaceOrigins,
    string TenantConnectionTemplate,
    string CatalogConnectionString)
{
    public static BrowserAcceptanceConfiguration Create(SqlServerAcceptanceRun run, int apiPort, int frontendPort)
    {
        if (apiPort is < 1024 or > 65535 || frontendPort is < 1024 or > 65535 || apiPort == frontendPort)
            throw new InvalidOperationException("Browser acceptance ports must be distinct user ports.");

        run.ValidateDestinations(run.TenantDatabaseNames.Append(run.CatalogDatabaseName));
        return new(
            apiPort,
            frontendPort,
            $"http://localhost:{apiPort}",
            $"http://localhost:{frontendPort}",
            [$"http://tenant-a.localhost:{frontendPort}", $"http://tenant-b.localhost:{frontendPort}"],
            $"Server={run.Server};Database={run.TenantDatabaseNames[0]};Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
            $"Server={run.Server};Database={run.CatalogDatabaseName};Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
    }
}
