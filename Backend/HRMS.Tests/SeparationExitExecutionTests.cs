using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SeparationExitExecutionTests
{
    [Fact]
    public async Task Exit_execution_applies_authoritative_lwd_and_is_idempotent()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await ReadyFixtureAsync(database);

        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = new SeparationExitService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System);
        var first = await service.ExecuteAsync(fixture.SeparationId, new(ExpectedConcurrencyVersion: 0, IdempotencyKey: "exit-1"));
        Assert.True(first.Succeeded, first.Message);

        await using var verify = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var employee = await verify.Employees.SingleAsync(x => x.Id == fixture.EmployeeId);
        var separation = await verify.EmployeeSeparations.SingleAsync(x => x.Id == fixture.SeparationId);
        Assert.Equal(EmployeeStatus.Terminated, employee.Status);
        Assert.Equal(fixture.FinalLwd, employee.DateOfLeaving);
        Assert.Equal(EmployeeSeparationStatus.Closed, separation.Status);
        Assert.Equal(1, await verify.SeparationExitExecutions.CountAsync());
        Assert.Equal(1, await verify.SeparationExitExecutionEvents.CountAsync(x => x.EventType == SeparationExitExecutionEventType.SeparationClosed));

        await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var retry = await new SeparationExitService(retryDb, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System)
            .RetryAsync(fixture.SeparationId, new("retry"));
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, await retryDb.SeparationExitExecutions.CountAsync());
        Assert.Equal(1, await retryDb.SeparationExitExecutionEvents.CountAsync(x => x.EventType == SeparationExitExecutionEventType.SeparationClosed));
    }

    [Fact]
    public async Task Readiness_blocks_exit_before_final_lwd()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await ReadyFixtureAsync(database, DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var readiness = await new SeparationExitService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System).GetReadinessAsync(fixture.SeparationId);
        Assert.True(readiness.Succeeded, readiness.Message);
        Assert.Contains(readiness.Value!.Blockers, x => x.Code == "LwdNotReached");
    }

    [Fact]
    public async Task Exit_execution_deactivates_tenant_account_revokes_sessions_and_preserves_history()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await ReadyFixtureAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = fixture.EmployeeUserId, TokenHash = "hash-1", ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        var result = await new SeparationExitService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System).ExecuteAsync(fixture.SeparationId, new());
        Assert.True(result.Succeeded, result.Message);

        await using var verify = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        Assert.False((await verify.Users.SingleAsync(x => x.Id == fixture.EmployeeUserId)).IsActive);
        Assert.NotNull(await verify.RefreshTokens.SingleAsync(x => x.UserId == fixture.EmployeeUserId));
        Assert.NotNull((await verify.RefreshTokens.SingleAsync(x => x.UserId == fixture.EmployeeUserId)).RevokedAtUtc);
        Assert.NotEmpty(await verify.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync());
        Assert.NotEmpty(await verify.AccountEmployeeLinkEvents.Where(x => x.AfterEmployeeId == fixture.EmployeeId).ToListAsync());
    }

    [Fact]
    public async Task Closed_separation_cannot_be_executed_again()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await ReadyFixtureAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = new SeparationExitService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System);
        Assert.True((await service.ExecuteAsync(fixture.SeparationId, new())).Succeeded);
        var second = await service.ExecuteAsync(fixture.SeparationId, new());
        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(1, await db.SeparationExitExecutionEvents.CountAsync(x => x.EventType == SeparationExitExecutionEventType.SeparationClosed));
    }

    internal static async Task<ExitFixture> ReadyFixtureAsync(SqliteInMemoryDatabase database, DateOnly? finalLwd = null)
    {
        var lwd = finalLwd ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var baseFixture = await NoticeTestData.CreateApprovedAsync(database, lwd);
        await using var db = database.CreateContext(new TestTenantContext(baseFixture.TenantId, baseFixture.HrUserId));
        var clearance = new SeparationClearance { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, EmployeeSeparationId = baseFixture.SeparationId, EmployeeId = baseFixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow };
        var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, EmployeeId = baseFixture.EmployeeId, SeparationDate = lwd, LastWorkingDate = lwd, SettlementDate = lwd, Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
        db.SeparationClearances.Add(clearance);
        db.FinalSettlementCases.Add(settlement);
        db.SeparationSettlementOrchestrations.Add(new SeparationSettlementOrchestration { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, EmployeeSeparationId = baseFixture.SeparationId, EmployeeId = baseFixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
        db.SeparationExitInterviews.Add(new SeparationExitInterview { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, EmployeeSeparationId = baseFixture.SeparationId, EmployeeId = baseFixture.EmployeeId, TemplateVersionId = Guid.NewGuid(), Status = SeparationExitInterviewStatus.CompletedWithoutEmployeeResponse, CompletedWithoutEmployeeResponse = true, HrCompletedAtUtc = DateTime.UtcNow, FinalizedAtUtc = DateTime.UtcNow, AssignedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return new(baseFixture.TenantId, baseFixture.EmployeeId, baseFixture.EmployeeUserId, baseFixture.HrUserId, baseFixture.SeparationId, lwd);
    }

    internal sealed record ExitFixture(Guid TenantId, Guid EmployeeId, Guid EmployeeUserId, Guid HrUserId, Guid SeparationId, DateOnly FinalLwd);
}

internal static class SeparationExitProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, DatabaseProviderType provider)
    {
        Assert.Equal(provider == DatabaseProviderType.MySql, db.IsMySql);
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationExitExecution)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationExitExecutionEvent)));
        Assert.True(await db.Database.CanConnectAsync());
    }
}

public sealed class MySqlSeparationExitIntegrationTests
{
    [Fact]
    public async Task MySql_separation_exit_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Phase 8H tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var name = $"HRMS_Phase8H_Exit_{Guid.NewGuid():N}";
        var admin = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(normalized) { Database = name };
        try
        {
            await using (var server = new MySql.Data.MySqlClient.MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var command = server.CreateCommand(); command.CommandText = $"CREATE DATABASE `{name}`"; await command.ExecuteNonQueryAsync(); }
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());
            await db.Database.MigrateAsync();
            await SeparationExitProviderAcceptance.RunAsync(db, DatabaseProviderType.MySql);
        }
        finally { await using var server = new MySql.Data.MySqlClient.MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var command = server.CreateCommand(); command.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await command.ExecuteNonQueryAsync(); }
    }
}

public sealed class SqlServerSeparationExitIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSeparationExitIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerSeparationExitFact]
    public async Task SqlServer_separation_exit_provider_acceptance() { await using var db = fixture.CreateContext(new TestTenantContext(Guid.NewGuid())); await SeparationExitProviderAcceptance.RunAsync(db, DatabaseProviderType.SqlServer); }
}

public sealed class SqlServerSeparationExitFactAttribute : FactAttribute
{
    public SqlServerSeparationExitFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Phase 8H tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
