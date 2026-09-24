using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Sdk;
using Xunit.Abstractions;

namespace HRMS.Tests;

internal static class SeparationExitInterviewProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        Assert.Equal(provider == DatabaseProviderType.MySql, db.IsMySql);
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationExitInterviewTemplate)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationExitInterview)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationExitInterviewResponseRevision)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationExitInterviewHrNote)));
        Assert.Empty(await db.SeparationExitInterviews.ToListAsync());
    }
}

public sealed class MySqlSeparationExitInterviewIntegrationTests
{
    private readonly ITestOutputHelper output;
    public MySqlSeparationExitInterviewIntegrationTests(ITestOutputHelper output) => this.output = output;

    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_separation_exit_interview_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Phase 8E tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var name = $"HRMS_Phase8E_Exit_{Guid.NewGuid():N}"; var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("HRMS_PHASE8E_MYSQL_TIMEOUT_SECONDS"), out var configuredTimeoutSeconds) ? configuredTimeoutSeconds : 180;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { output.WriteLine("Phase 8E MySQL: opening admin connection."); await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(timeout.Token); output.WriteLine("Phase 8E MySQL: admin connection opened; creating database."); await using var create = server.CreateCommand(); create.CommandTimeout = 90; create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(timeout.Token); } output.WriteLine("Phase 8E MySQL: database created; migrating."); var tenant = new TestTenantContext(); await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant); var pending = (await db.Database.GetPendingMigrationsAsync(timeout.Token)).ToArray(); output.WriteLine($"Phase 8E MySQL: pending migrations before migrate: {string.Join(", ", pending)}"); try { await db.Database.MigrateAsync(timeout.Token); } catch (OperationCanceledException) { var appliedAfterCancellation = (await db.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray(); output.WriteLine($"Phase 8E MySQL: applied migrations after cancellation: {string.Join(", ", appliedAfterCancellation)}"); output.WriteLine($"Phase 8E MySQL: migration active at cancellation: {pending.FirstOrDefault(m => !appliedAfterCancellation.Contains(m)) ?? "unknown"}"); throw; } var applied = (await db.Database.GetAppliedMigrationsAsync(timeout.Token)).ToArray(); output.WriteLine($"Phase 8E MySQL: migration complete; last applied migration: {applied.LastOrDefault() ?? "none"}"); output.WriteLine("Phase 8E MySQL: running acceptance."); await SeparationExitInterviewProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.MySql); output.WriteLine("Phase 8E MySQL: acceptance complete."); }
        finally { output.WriteLine("Phase 8E MySQL: opening admin connection for cleanup."); await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); output.WriteLine("Phase 8E MySQL: dropping database."); await using var drop = server.CreateCommand(); drop.CommandTimeout = 90; drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync(); output.WriteLine("Phase 8E MySQL: cleanup complete."); }
    }
}

public sealed class SqlServerSeparationExitInterviewIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSeparationExitInterviewIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerSeparationExitInterviewFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_separation_exit_interview_provider_acceptance()
    { var tenant = new TestTenantContext(Guid.NewGuid()); await using var db = fixture.CreateContext(tenant); await SeparationExitInterviewProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); }
}

public sealed class SqlServerSeparationExitInterviewFactAttribute : FactAttribute
{
    public SqlServerSeparationExitInterviewFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Phase 8E tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
