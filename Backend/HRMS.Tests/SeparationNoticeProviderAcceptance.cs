using HRMS.Infrastructure.Persistence;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class SeparationNoticeProviderAcceptance
{
    public static Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
        => SeparationFoundationProviderAcceptance.RunAsync(db, tenant, provider);
}

public sealed class MySqlSeparationNoticeIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_separation_notice_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Phase 8C tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var name = $"HRMS_Phase8C_SeparationNotice_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString))
            {
                await server.OpenAsync();
                await using var create = server.CreateCommand();
                create.CommandText = $"CREATE DATABASE `{name}`";
                await create.ExecuteNonQueryAsync();
            }
            var tenant = new TestTenantContext();
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
            await db.Database.MigrateAsync();
            await SeparationNoticeProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.MySql);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString);
            await server.OpenAsync();
            await using var drop = server.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`";
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class SqlServerSeparationNoticeIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSeparationNoticeIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerSeparationNoticeFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_separation_notice_provider_acceptance()
    {
        var tenant = new TestTenantContext(Guid.NewGuid());
        await using var db = fixture.CreateContext(tenant);
        await SeparationNoticeProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}

public sealed class SqlServerSeparationNoticeFactAttribute : FactAttribute
{
    public SqlServerSeparationNoticeFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable))
        ? $"SQL Server Phase 8C tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent."
        : null;
}
