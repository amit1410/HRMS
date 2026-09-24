using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class SeparationDocumentProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, DatabaseProviderType provider)
    {
        Assert.Equal(provider == DatabaseProviderType.MySql, db.IsMySql);
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentTemplate)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentTemplateVersion)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationGeneratedDocument)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentEvent)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentNumberSequence)));
        Assert.Empty(await db.SeparationGeneratedDocuments.ToListAsync());
    }
}

public sealed class MySqlSeparationDocumentIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_separation_document_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Phase 8G tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent."); var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var name = $"HRMS_Phase8G_Document_{Guid.NewGuid():N}"; var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var connection = new MySqlConnectionStringBuilder(normalized) { Database = name }; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        try { await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(timeout.Token); await using var create = server.CreateCommand(); create.CommandTimeout = 90; create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(timeout.Token); } await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext()); await db.Database.MigrateAsync(timeout.Token); await SeparationDocumentProviderAcceptance.RunAsync(db, DatabaseProviderType.MySql); }
        finally { await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandTimeout = 90; drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync(); }
    }
}

public sealed class SqlServerSeparationDocumentIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSeparationDocumentIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerSeparationDocumentFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_separation_document_provider_acceptance() { await using var db = fixture.CreateContext(new TestTenantContext(Guid.NewGuid())); await SeparationDocumentProviderAcceptance.RunAsync(db, DatabaseProviderType.SqlServer); }
}

public sealed class SqlServerSeparationDocumentFactAttribute : FactAttribute
{
    public SqlServerSeparationDocumentFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Phase 8G tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
