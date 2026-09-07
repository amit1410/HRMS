using System.Text.Json;
using HRMS.Tests.TestSupport;

if (args.Length is < 2 or > 3 || args[0] is not ("setup" or "cleanup"))
{
    Console.Error.WriteLine("Usage: dotnet run -- setup <state-path> | cleanup <state-path>");
    return 2;
}

var command = args[0];
var statePath = Path.GetFullPath(args[1]);

if (command == "setup")
{
    SqlServerPhase3BFixture? fixture = null;
    try
    {
        fixture = new SqlServerPhase3BFixture();
        await fixture.InitializeAsync();
        var run = fixture.Run ?? throw new InvalidOperationException("SQL Server configuration is absent.");
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new
        {
            runId = run.RunId,
            server = run.Server,
            manifestPath = run.ManifestPath,
            catalogDatabase = run.CatalogDatabaseName,
            tenantDatabases = run.TenantDatabaseNames,
            tenantBranding = new
            {
                tenantA = SqlServerPhase3BFixture.TenantADisplayName,
                tenantB = SqlServerPhase3BFixture.TenantBDisplayName
            }
            , syntheticPassword = fixture.SyntheticPassword
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SETUP_COMPLETE state={statePath} manifest={run.ManifestPath}");
        // The databases intentionally remain available for the separately started browser processes.
        fixture = null;
        return 0;
    }
    catch
    {
        if (fixture is not null)
            await fixture.DisposeAsync();
        throw;
    }
}

var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath)).RootElement;
var server = state.GetProperty("server").GetString()!;
var runId = state.GetProperty("runId").GetString()!;
var manifestPath = state.GetProperty("manifestPath").GetString()!;
var ownedRun = SqlServerAcceptanceRun.Create(server, runId, manifestPath);
await ownedRun.DropDatabasesAsync();
Console.WriteLine($"CLEANUP_COMPLETE state={statePath}");
return 0;
