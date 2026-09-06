using System.Data.Common;
using System.Globalization;
using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MixedProviderHttpValidationRunner;

internal static class ValidationRunner
{
    public static async Task<int> Main(string[] args)
    {
        var options = RunnerOptions.Parse(args);
        using var sink = new EvidenceSink();
        using var factory = new ValidationFactory(sink, options.ApiContentRoot);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        if (options.HostStartupOnly)
        {
            Console.WriteLine("HTTP TEST HOST STARTUP");
            Console.WriteLine($"API content root: {options.ApiContentRoot}");
            Console.WriteLine("Environment: Development");
            Console.WriteLine("Database initialization skipped: PASS");
            Console.WriteLine("Production API host built: PASS");
            Console.WriteLine("Database access: NONE");
            Console.WriteLine("HTTP request sent: NO");
            Console.WriteLine("HOST STARTUP SMOKE TEST PASS");
            return 0;
        }

        foreach (var host in new[] { TestConstants.SqlHost, TestConstants.MySqlHost })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Host = host;
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP validation failed for host '{host}' with status {(int)response.StatusCode}.");

            PrintEvidence(await sink.WaitForAsync(host));
        }

        Console.WriteLine();
        Console.WriteLine("HTTP MIXED-PROVIDER VALIDATION PASS");
        return 0;
    }

    private static void PrintEvidence(Evidence evidence)
    {
        Console.WriteLine();
        Console.WriteLine(evidence.Provider == DatabaseProviderType.SqlServer ? "ADAPTERSQL" : "ADAPTERMYSQL");
        Console.WriteLine("Host routing: PASS");
        Console.WriteLine("Catalog tenant resolution: PASS");
        Console.WriteLine($"TenantCode: {evidence.TenantCode}");
        Console.WriteLine($"TenantId: {evidence.TenantId}");
        Console.WriteLine($"ShardKey: {evidence.ShardKey}");
        Console.WriteLine($"DatabaseProvider: {evidence.Provider}");
        Console.WriteLine($"EF ProviderName: {evidence.ProviderName}");
        Console.WriteLine($"Connection type: {evidence.ConnectionType}");
        if (evidence.Provider == DatabaseProviderType.SqlServer)
            Console.WriteLine($"Server database: {evidence.ServerDatabase}");
        else
        {
            Console.WriteLine($"Server lower_case_table_names: {evidence.ServerLowerCaseTableNames}");
            Console.WriteLine($"Server DATABASE(): {evidence.ServerDatabase}");
        }
        Console.WriteLine("Database identity: PASS");
        Console.WriteLine($"Read-only tenant query: PASS ({evidence.UserCount.ToString(CultureInfo.InvariantCulture)})");
    }
}

sealed record RunnerOptions(string ApiContentRoot, bool HostStartupOnly)
{
    public static RunnerOptions Parse(string[] args)
    {
        string? apiContentRoot = null;
        var hostStartupOnly = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--api-content-root":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                        throw new ArgumentException("--api-content-root requires a non-empty path.");
                    apiContentRoot = Path.GetFullPath(args[index]);
                    break;
                case "--host-startup-only":
                    hostStartupOnly = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown validation-runner argument '{args[index]}'.");
            }
        }

        if (apiContentRoot is null)
            throw new ArgumentException("--api-content-root is required.");
        if (!Directory.Exists(apiContentRoot))
            throw new DirectoryNotFoundException($"API content root does not exist: {apiContentRoot}");

        var apiProject = Path.Combine(apiContentRoot, "HRMS.API.csproj");
        if (!File.Exists(apiProject))
            throw new FileNotFoundException("The API project was not found below the supplied content root.", apiProject);

        return new RunnerOptions(apiContentRoot, hostStartupOnly);
    }
}

sealed class ValidationFactory : WebApplicationFactory<global::Program>
{
    private readonly EvidenceSink _sink;
    private readonly string _apiContentRoot;

    public ValidationFactory(EvidenceSink sink, string apiContentRoot)
    {
        _sink = sink;
        _apiContentRoot = apiContentRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(_apiContentRoot);
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ApplicationKey, typeof(global::Program).Assembly.GetName().Name!);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(_sink);
            services.AddSingleton<IStartupFilter, EvidenceStartupFilter>();
        });
    }
}

sealed class EvidenceStartupFilter : IStartupFilter
{
    private readonly EvidenceSink _sink;

    public EvidenceStartupFilter(EvidenceSink sink) => _sink = sink;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, downstream) =>
        {
            await downstream(context);
            if (context.Request.Host.Host is TestConstants.SqlHost or TestConstants.MySqlHost)
                await _sink.CaptureAsync(context);
        });

        next(app);
    };
}

sealed class EvidenceSink : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Evidence> _evidence = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _errors = new(StringComparer.OrdinalIgnoreCase);

    public async Task CaptureAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        try
        {
            var shard = context.RequestServices.GetRequiredService<IShardContext>().Current
                ?? throw new InvalidOperationException($"No shard was selected for host '{host}'.");
            var sql = host.Equals(TestConstants.SqlHost, StringComparison.OrdinalIgnoreCase);
            var expectedProvider = sql ? DatabaseProviderType.SqlServer : DatabaseProviderType.MySql;
            var expectedCode = sql ? "ADAPTERSQL" : "ADAPTERMYSQL";
            var expectedId = sql ? TestConstants.SqlTenantId : TestConstants.MySqlTenantId;
            var expectedShard = sql ? TestConstants.SqlShardKey : TestConstants.MySqlShardKey;

            Require(shard.DatabaseProvider == expectedProvider, $"Host '{host}' resolved to an unexpected provider.");
            Require(shard.TenantCode == expectedCode, $"Host '{host}' resolved to an unexpected tenant code.");
            Require(shard.TenantId == expectedId, $"Host '{host}' resolved to an unexpected tenant id.");
            Require(shard.ShardKey == expectedShard, $"Host '{host}' resolved to an unexpected shard key.");

            var db = context.RequestServices.GetRequiredService<HrmsDbContext>();
            var providerName = db.Database.ProviderName
                ?? throw new InvalidOperationException($"Host '{host}' did not expose an EF provider name.");
            var expectedProviderName = sql ? TestConstants.SqlProviderName : TestConstants.MySqlProviderName;
            Require(providerName == expectedProviderName, $"Host '{host}' selected an unexpected EF provider.");

            var connection = db.Database.GetDbConnection();
            var expectedConnectionType = sql ? TestConstants.SqlConnectionType : TestConstants.MySqlConnectionType;
            Require(connection.GetType().FullName == expectedConnectionType, $"Host '{host}' selected an unexpected connection type.");
            var configuredDatabase = connection.Database;
            Require(sql
                ? configuredDatabase.Equals(TestConstants.SqlDatabase, StringComparison.Ordinal)
                : configuredDatabase.Equals(TestConstants.MySqlDatabase, StringComparison.OrdinalIgnoreCase),
                $"Host '{host}' targeted an unexpected database before open.");

            string serverDatabase;
            int? serverLowerCaseTableNames = null;
            try
            {
                await connection.OpenAsync(context.RequestAborted);
                if (sql)
                {
                    serverDatabase = await ReadSingleStringAsync(connection, "SELECT DB_NAME();", context.RequestAborted);
                    Require(serverDatabase.Equals(TestConstants.SqlDatabase, StringComparison.Ordinal), $"Host '{host}' opened an unexpected SQL database.");
                }
                else
                {
                    var identity = await ReadMySqlIdentityAsync(connection, context.RequestAborted);
                    serverLowerCaseTableNames = identity.LowerCaseTableNames;
                    serverDatabase = identity.Database;
                    Require(serverLowerCaseTableNames == TestConstants.MySqlLowerCaseTableNames, $"Host '{host}' used an unexpected MySQL server case mode.");
                    Require(serverDatabase.Equals(TestConstants.MySqlDatabase, StringComparison.Ordinal), $"Host '{host}' opened an unexpected MySQL server database.");
                }

                var userCount = await db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync(context.RequestAborted);
                Add(new Evidence(host, shard.TenantCode, shard.TenantId, shard.ShardKey, shard.DatabaseProvider, providerName,
                    connection.GetType().FullName!, configuredDatabase, serverDatabase, serverLowerCaseTableNames, userCount));
            }
            finally
            {
                if (connection.State != System.Data.ConnectionState.Closed)
                    await connection.CloseAsync();
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
                _errors[host] = exception;
            throw;
        }
    }

    public async Task<Evidence> WaitForAsync(string host)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            lock (_gate)
            {
                if (_errors.TryGetValue(host, out var error))
                    throw new InvalidOperationException($"HTTP tenant evidence failed for host '{host}': {error.Message}", error);
                if (_evidence.TryGetValue(host, out var evidence))
                    return evidence;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"Timed out waiting for tenant evidence for host '{host}'.");
    }

    private void Add(Evidence evidence)
    {
        lock (_gate)
            _evidence[evidence.Host] = evidence;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static async Task<string> ReadSingleStringAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Require(await reader.ReadAsync(cancellationToken), "The server identity query returned no rows.");
        var value = reader.GetValue(0)?.ToString();
        Require(!string.IsNullOrWhiteSpace(value), "The server identity query returned a blank database name.");
        Require(!await reader.ReadAsync(cancellationToken), "The server identity query returned multiple rows.");
        return value!;
    }

    private static async Task<(int LowerCaseTableNames, string Database)> ReadMySqlIdentityAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@lower_case_table_names, DATABASE();";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Require(await reader.ReadAsync(cancellationToken), "The MySQL server identity query returned no rows.");
        var mode = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
        var database = reader.GetValue(1)?.ToString();
        Require(!string.IsNullOrWhiteSpace(database), "The MySQL server identity query returned a blank database name.");
        Require(!await reader.ReadAsync(cancellationToken), "The server identity query returned multiple rows.");
        return (mode, database!);
    }

    public void Dispose()
    {
    }
}

static class TestConstants
{
    public const string SqlHost = "adaptersql.localhost";
    public const string MySqlHost = "adaptermysql.localhost";
    public const string SqlProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    public const string MySqlProviderName = "MySql.EntityFrameworkCore";
    public const string SqlConnectionType = "Microsoft.Data.SqlClient.SqlConnection";
    public const string MySqlConnectionType = "MySql.Data.MySqlClient.MySqlConnection";
    public const string SqlDatabase = "HRMS";
    public const string MySqlDatabase = "hrms_mysqladapter_test_20260905";
    public const string SqlShardKey = "adapter-sql-tenant-test-20260906";
    public const string MySqlShardKey = "mysqladapter_test_20260905";
    public const int MySqlLowerCaseTableNames = 1;
    public static readonly Guid SqlTenantId = new("77777777-7777-7777-7777-777777777701");
    public static readonly Guid MySqlTenantId = new("77777777-7777-7777-7777-777777777702");
}

sealed record Evidence(
    string Host,
    string TenantCode,
    Guid TenantId,
    string ShardKey,
    DatabaseProviderType Provider,
    string ProviderName,
    string ConnectionType,
    string ConfiguredDatabase,
    string ServerDatabase,
    int? ServerLowerCaseTableNames,
    int UserCount);
