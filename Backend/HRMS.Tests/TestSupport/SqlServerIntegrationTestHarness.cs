using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
            ? new(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlServer(_connectionString).Options, tenantContext)
            : throw new InvalidOperationException("SQL Server integration test connection is not configured.");
}
