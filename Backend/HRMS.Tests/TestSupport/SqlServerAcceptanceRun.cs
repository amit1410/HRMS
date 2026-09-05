using System.Data;
using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests.TestSupport;

/// <summary>Plans and owns one disposable SQL Server acceptance run. Planning is connection-free.</summary>
public sealed class SqlServerAcceptanceRun
{
    public const string ServerEnvironmentVariable = "HRMS_SQLSERVER_TEST_SERVER";
    public const string AuthEnvironmentVariable = "HRMS_SQLSERVER_TEST_AUTH";
    public const string ManifestEnvironmentVariable = "HRMS_PHASE3B_MANIFEST_PATH";
    public const string Prefix = "HRMS_Phase3B_Integration_";

    private static readonly string[] ProtectedNames = ["master", "model", "msdb", "tempdb", "HRMS", "HRMS_Catalog"];

    private SqlServerAcceptanceRun(string server, string runId, string manifestPath)
    {
        Server = server;
        RunId = runId;
        ManifestPath = manifestPath;
        CatalogDatabaseName = $"{Prefix}{runId}_catalog";
        TenantDatabaseNames = [$"{Prefix}{runId}_tenanta", $"{Prefix}{runId}_tenantb"];
    }

    public string Server { get; }
    public string RunId { get; }
    public string ManifestPath { get; }
    public string CatalogDatabaseName { get; }
    public IReadOnlyList<string> TenantDatabaseNames { get; }
    public IReadOnlyList<string> AllDatabaseNames => [CatalogDatabaseName, .. TenantDatabaseNames];

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ServerEnvironmentVariable));

    public static SqlServerAcceptanceRun? FromEnvironment()
    {
        var server = Environment.GetEnvironmentVariable(ServerEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(server)) return null;
        var auth = Environment.GetEnvironmentVariable(AuthEnvironmentVariable);
        if (!string.Equals(auth, "Integrated", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{AuthEnvironmentVariable} must be 'Integrated'. Password authentication is not supported.");
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "_" + Random.Shared.Next(100000, 999999);
        var configuredPath = Environment.GetEnvironmentVariable(ManifestEnvironmentVariable);
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "phase3b-acceptance", $"{Prefix}{runId}.json")
            : Path.GetFullPath(configuredPath);
        return Create(server.Trim(), runId, path);
    }

    public static SqlServerAcceptanceRun Create(string server, string runId, string manifestPath)
    {
        ValidateServer(server);
        ValidateRunId(runId);
        var run = new SqlServerAcceptanceRun(server, runId, manifestPath);
        foreach (var database in run.AllDatabaseNames)
            ValidateDatabaseName(database, run.RunId, run.AllDatabaseNames);
        return run;
    }

    public static void ValidateServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) throw new InvalidOperationException("SQL Server test server is missing.");
        if (server.Contains(';') || server.Contains('=') || server.Contains('"'))
            throw new InvalidOperationException("SQL Server test server must be a server/instance name, not a connection string.");
    }

    public static void ValidateRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || !System.Text.RegularExpressions.Regex.IsMatch(runId, @"^\d{8}T\d{6}Z_[0-9]{6}$"))
            throw new InvalidOperationException("The acceptance run id has an invalid format.");
    }

    public static void ValidateDatabaseName(string? database, string runId, IReadOnlyCollection<string>? ownedNames = null)
    {
        if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("Database name is missing.");
        if (ProtectedNames.Contains(database, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing protected database '{database}'.");
        if (!database.StartsWith(Prefix, StringComparison.Ordinal) ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                database,
                $"^{System.Text.RegularExpressions.Regex.Escape(Prefix + runId)}_(catalog|tenanta|tenantb)$"))
            throw new InvalidOperationException($"Database '{database}' is not owned by this Phase 3B run.");
        if (ownedNames is not null && !ownedNames.Contains(database, StringComparer.Ordinal))
            throw new InvalidOperationException($"Database '{database}' is not in the run manifest.");
    }

    public SqlConnectionStringBuilder Connection(string database)
    {
        ValidateDatabaseName(database, RunId, AllDatabaseNames);
        return new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 10,
            CommandTimeout = 30,
            ApplicationName = $"HRMS Phase 3B {RunId}"
        };
    }

    public SqlConnectionStringBuilder MasterConnection() => new()
    {
        DataSource = Server,
        InitialCatalog = "master",
        IntegratedSecurity = true,
        Encrypt = true,
        TrustServerCertificate = true,
        ConnectTimeout = 10,
        ApplicationName = $"HRMS Phase 3B {RunId}"
    };

    public string TenantDatabase(int index) => TenantDatabaseNames[index];

    public HrmsDbContext CreateTenantContext(int index, ITenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlServer(Connection(TenantDatabase(index)).ConnectionString, sql => sql.CommandTimeout(5)).Options, tenantContext);

    public HrmsCatalogDbContext CreateCatalogContext() =>
        new(new DbContextOptionsBuilder<HrmsCatalogDbContext>().UseSqlServer(Connection(CatalogDatabaseName).ConnectionString).Options);

    public void WriteManifest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        var manifest = new { runId = RunId, server = Server, catalogDatabase = CatalogDatabaseName, tenantDatabases = TenantDatabaseNames, databases = AllDatabaseNames };
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task CreateDatabasesAsync(CancellationToken cancellationToken = default)
    {
        WriteManifest();
        await using var master = new SqlConnection(MasterConnection().ConnectionString);
        await master.OpenAsync(cancellationToken);
        foreach (var database in AllDatabaseNames)
        {
            ValidateDatabaseName(database, RunId, AllDatabaseNames);
            if (await ExistsAsync(master, database, cancellationToken))
                throw new InvalidOperationException($"Refusing existing acceptance database '{database}'.");
            await using var command = master.CreateCommand();
            command.CommandText = $"CREATE DATABASE {QuoteIdentifier(database)}";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using (var catalog = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>().UseSqlServer(Connection(CatalogDatabaseName).ConnectionString).Options))
            await catalog.Database.MigrateAsync(cancellationToken);
        foreach (var database in TenantDatabaseNames)
        {
            await using var tenant = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlServer(Connection(database).ConnectionString).Options, new TestTenantContext());
            await tenant.Database.MigrateAsync(cancellationToken);
        }
    }

    public async Task DropDatabasesAsync(CancellationToken cancellationToken = default)
    {
        ValidateManifestOwnership();
        await using var master = new SqlConnection(MasterConnection().ConnectionString);
        await master.OpenAsync(cancellationToken);
        foreach (var database in AllDatabaseNames.Reverse())
        {
            ValidateDatabaseName(database, RunId, AllDatabaseNames);
            if (!await ExistsAsync(master, database, cancellationToken)) continue;
            await using var command = master.CreateCommand();
            command.CommandText = "DROP DATABASE " + QuoteIdentifier(database);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>Validates all startup destinations before an isolated host is allowed to open them.</summary>
    public void ValidateDestinations(IEnumerable<string> destinations)
    {
        var names = destinations.ToArray();
        if (names.Length == 0) throw new InvalidOperationException("No SQL Server acceptance destinations were supplied.");
        foreach (var database in names) ValidateDatabaseName(database, RunId, AllDatabaseNames);
    }

    /// <summary>Reads the sanitized manifest and prevents cleanup of a different run or server.</summary>
    public void ValidateManifestOwnership()
    {
        if (!File.Exists(ManifestPath)) throw new InvalidOperationException("The acceptance ownership manifest is missing.");
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("runId").GetString(), RunId, StringComparison.Ordinal) ||
            !string.Equals(root.GetProperty("server").GetString(), Server, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The acceptance manifest does not belong to this run and server.");
        var names = root.GetProperty("databases").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
        if (!names.SequenceEqual(AllDatabaseNames, StringComparer.Ordinal))
            throw new InvalidOperationException("The acceptance manifest database set does not match this run.");
    }

    private static async Task<bool> ExistsAsync(SqlConnection master, string database, CancellationToken cancellationToken)
    {
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = database });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static string QuoteIdentifier(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
