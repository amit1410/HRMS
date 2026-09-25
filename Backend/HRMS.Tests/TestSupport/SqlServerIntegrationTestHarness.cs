using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace HRMS.Tests.TestSupport;

/// <summary>Dedicated SQL Server fixture. Never permits application or system databases.</summary>
public sealed class SqlServerIntegrationTestHarness : IAsyncLifetime
{
    public const string RequiredEnvironmentVariable = "HRMS_SQLSERVER_TEST_CONNECTION";
    private static readonly string[] Forbidden = ["HRMS", "master", "model", "msdb", "tempdb"];
    private readonly string _connectionString;
    public bool IsConfigured { get; }

    public SqlServerIntegrationTestHarness()
    {
        _connectionString = Environment.GetEnvironmentVariable(RequiredEnvironmentVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString)) return;
        var database = new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
        ValidateIntegrationDatabase(database);
        DatabaseName = database;
        IsConfigured = true;
    }

    public string DatabaseName { get; } = string.Empty;
    public static void ValidateIntegrationDatabase(string? database)
    {
        database = database?.Trim();
        if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("SQL Server integration test database name is missing.");
        if (Forbidden.Contains(database, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"Refusing to execute integration tests against protected database '{database}'.");
        if (!database.Contains("Test", StringComparison.OrdinalIgnoreCase) && !database.Contains("Integration", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Integration test database '{database}' must contain 'Test' or 'Integration'.");
    }

    public async Task InitializeAsync()
    {
        if (!IsConfigured) return;
        await using var context = CreateContext(new TestTenantContext());
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public HrmsDbContext CreateContext(ITenantContext tenantContext) =>
        IsConfigured
            ? new(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlServer(_connectionString, sql => sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName)).Options, tenantContext)
            : throw new InvalidOperationException("SQL Server integration test connection is not configured.");

    public async Task<SqlServerDisposableDatabase> CreateDisposableDatabaseAsync(string ownedPrefix)
    {
        if (!IsConfigured) throw new InvalidOperationException("SQL Server integration test connection is not configured.");
        if (string.IsNullOrWhiteSpace(ownedPrefix) || !Regex.IsMatch(ownedPrefix, "^[A-Za-z][A-Za-z0-9_]*$"))
            throw new ArgumentException("Disposable database prefix must contain only letters, digits, and underscores.", nameof(ownedPrefix));
        if (!ownedPrefix.Contains("Test", StringComparison.OrdinalIgnoreCase) && !ownedPrefix.Contains("Integration", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Disposable database prefix must contain 'Test' or 'Integration'.", nameof(ownedPrefix));

        var databaseName = $"{ownedPrefix}{Guid.NewGuid():N}";
        ValidateIntegrationDatabase(databaseName);
        var source = new SqlConnectionStringBuilder(_connectionString);
        var admin = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = "master" };
        var target = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = databaseName };
        await using (var connection = new SqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        var disposable = new SqlServerDisposableDatabase(admin.ConnectionString, target.ConnectionString, databaseName, ownedPrefix);
        try
        {
            await using var db = disposable.CreateContext(new TestTenantContext());
            await db.Database.MigrateAsync();
            return disposable;
        }
        catch
        {
            await disposable.DisposeAsync();
            throw;
        }
    }

    public sealed class SqlServerDisposableDatabase : IAsyncDisposable
    {
        private readonly string _adminConnection;
        private readonly string _targetConnection;
        private readonly string _ownedPrefix;
        private bool _disposed;

        internal SqlServerDisposableDatabase(string adminConnection, string targetConnection, string databaseName, string ownedPrefix)
        {
            _adminConnection = adminConnection;
            _targetConnection = targetConnection;
            DatabaseName = databaseName;
            _ownedPrefix = ownedPrefix;
        }

        public string DatabaseName { get; }

        public HrmsDbContext CreateContext(ITenantContext tenantContext) =>
            new(new DbContextOptionsBuilder<HrmsDbContext>()
                .UseSqlServer(_targetConnection, sql => sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName))
                .Options, tenantContext);

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            if (!DatabaseName.StartsWith(_ownedPrefix, StringComparison.Ordinal) ||
                (!DatabaseName.Contains("Test", StringComparison.OrdinalIgnoreCase) && !DatabaseName.Contains("Integration", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Refusing to drop a database not owned by this SQL integration fixture.");

            await using var connection = new SqlConnection(_adminConnection);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END";
            await drop.ExecuteNonQueryAsync();
            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
            verify.Parameters.AddWithValue("@name", DatabaseName);
            if (Convert.ToInt32(await verify.ExecuteScalarAsync()) != 0)
                throw new InvalidOperationException("Disposable SQL Server database cleanup could not be verified.");
            _disposed = true;
        }
    }
}
