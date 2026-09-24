using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class SeparationSettlementProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        Assert.Equal(provider == DatabaseProviderType.MySql, db.IsMySql);
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationSettlementOrchestration)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationSettlementEvent)));
        Assert.Empty(await db.SeparationSettlementOrchestrations.ToListAsync());
        Assert.Empty(await db.SeparationSettlementEvents.ToListAsync());
    }
}

public sealed class MySqlSeparationSettlementIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_separation_settlement_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Phase 8F tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var name = $"HRMS_Phase8F_Settlement_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(timeout.Token); await using var create = server.CreateCommand(); create.CommandTimeout = 90; create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(timeout.Token); }
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());
            await db.Database.MigrateAsync(timeout.Token);
            await SeparationSettlementProviderAcceptance.RunAsync(db, new TestTenantContext(), DatabaseProviderType.MySql);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandTimeout = 90; drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class SqlServerSeparationSettlementIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSeparationSettlementIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerSeparationSettlementFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_separation_settlement_provider_acceptance()
    { var tenant = new TestTenantContext(Guid.NewGuid()); await using var db = fixture.CreateContext(tenant); await SeparationSettlementProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); }
}

public sealed class SqlServerSeparationSettlementFactAttribute : FactAttribute
{
    public SqlServerSeparationSettlementFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Phase 8F tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
