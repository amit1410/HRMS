using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class SeparationClearanceProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        Assert.True(db.Model.FindEntityType(typeof(HRMS.Domain.Entities.Separation.SeparationClearance)) is not null);
        Assert.True(db.Model.FindEntityType(typeof(HRMS.Domain.Entities.Separation.SeparationClearanceTask)) is not null);
        Assert.True(db.Model.FindEntityType(typeof(HRMS.Domain.Entities.Separation.SeparationAssetReturn)) is not null);
        Assert.Equal(provider == DatabaseProviderType.MySql, db.IsMySql);
        Assert.Empty(await db.SeparationClearances.ToListAsync());
    }
}

public sealed class MySqlSeparationClearanceIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_separation_clearance_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Phase 8D tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var name = $"HRMS_Phase8D_Clearance_{Guid.NewGuid():N}"; var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try { await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var create = server.CreateCommand(); create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(); } var tenant = new TestTenantContext(); await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant); await db.Database.MigrateAsync(); await SeparationClearanceProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.MySql); }
        finally { await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync(); }
    }
}

public sealed class SqlServerSeparationClearanceIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSeparationClearanceIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerSeparationClearanceFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_separation_clearance_provider_acceptance()
    {
        var tenant = new TestTenantContext(Guid.NewGuid()); await using var db = fixture.CreateContext(tenant); await SeparationClearanceProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}

public sealed class SqlServerSeparationClearanceFactAttribute : FactAttribute
{
    public SqlServerSeparationClearanceFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Phase 8D tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
