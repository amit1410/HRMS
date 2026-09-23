using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Tests;

/// <summary>
/// Deterministic failure/retry proofs. Failures are induced by the test transaction
/// boundary or connector test double; production code has no failure switch.
/// </summary>
public sealed class PayrollProductionFailureInjectionTests
{
    [Fact]
    public async Task Finalization_failure_before_commit_is_safe()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var maker = Guid.NewGuid(); var checker = Guid.NewGuid(); var runId = await SeedApprovedRunAsync(database, tenantId, maker, checker);
        var injected = new ThrowBeforeFinalizationCommitInterceptor();
        await using (var db = database.CreateContext(new TestTenantContext(tenantId, checker), injected))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PayrollRunService(db, new TestTenantContext(tenantId, checker), TimeProvider.System).TransitionAsync(runId, PayrollRunStatus.Finalized));
        }
        await using (var verify = database.CreateContext(new TestTenantContext(tenantId, checker)))
        {
            Assert.Equal(PayrollRunStatus.Approved, await verify.PayrollRuns.Where(x => x.Id == runId).Select(x => x.Status).SingleAsync());
            Assert.Equal(0, await verify.PayrollRunHistories.CountAsync(x => x.PayrollRunId == runId && x.ChangeType == PayrollRunHistoryChangeType.Finalized));
        }
        await using var retryDb = database.CreateContext(new TestTenantContext(tenantId, checker));
        var retry = await new PayrollRunService(retryDb, new TestTenantContext(tenantId, checker), TimeProvider.System).TransitionAsync(runId, PayrollRunStatus.Finalized);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(PayrollRunStatus.Finalized, retry.Value!.Status);
        Assert.Single(await retryDb.PayrollRunHistories.Where(x => x.PayrollRunId == runId && x.ChangeType == PayrollRunHistoryChangeType.Finalized).ToListAsync());
    }

    [Fact]
    public async Task Durable_success_response_failure_retry_is_safe()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        await SeedAsync(database, tenantId, employeeId);
        await using (var db = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var service = new PayrollAdjustmentService(db, new TestTenantContext(tenantId), TimeProvider.System, new PayrollRunService(db, new TestTenantContext(tenantId), TimeProvider.System));
            var first = await service.CreateAsync(Request(employeeId, sourceId));
            Assert.True(first.Succeeded, first.Message);
            // The caller loses the response after the durable SaveChanges; replay uses
            // the canonical source-identity uniqueness guard.
        }
        await using var retryDb = database.CreateContext(new TestTenantContext(tenantId));
        var retryService = new PayrollAdjustmentService(retryDb, new TestTenantContext(tenantId), TimeProvider.System, new PayrollRunService(retryDb, new TestTenantContext(tenantId), TimeProvider.System));
        var replay = await retryService.CreateAsync(Request(employeeId, sourceId));
        Assert.False(replay.Succeeded);
        Assert.Single(await retryDb.PayrollAdjustments.Where(x => x.SourceId == sourceId).ToListAsync());
    }

    [Fact]
    public async Task External_connector_retryable_failure_is_safe()
    {
        await using var scenario = await FilingScenario.CreateAsync();
        var service = new StatutoryFilingService(scenario.Db, scenario.Tenant, TimeProvider.System, new PayrollApprovalGuard(scenario.Db, scenario.Tenant));
        var profile = await service.CreateConnectionProfileAsync(new StatutoryFilingConnectionProfileRequest { Name = "Failure injection connector", ConnectorType = StatutoryFilingConnectorType.Test, Endpoint = "test://failure", NonSecretConfigurationJson = "{\"outcome\":\"retryable\"}", EffectiveFrom = new(2026, 1, 1) });
        Assert.True(profile.Succeeded, profile.Message);
        var definition = await service.CreateDefinitionAsync(new StatutoryFilingDefinitionRequest { Code = "FAILURE-FILING", Name = "Failure injection filing", FilingType = "Generic", JurisdictionCode = "IN", DestinationType = StatutoryFilingDestinationType.HttpApi, ConnectionProfileId = profile.Value!.Id });
        Assert.True(definition.Succeeded, definition.Message);
        var run = await service.CreateRunAsync(new StatutoryFilingRunRequest { DefinitionId = definition.Value!.Id, FilingPeriod = "2026-09" });
        Assert.True(run.Succeeded, run.Message);
        Assert.True((await service.GenerateAsync(run.Value!.Id)).Succeeded);
        var package = await service.GetPackageAsync(run.Value.Id);
        Assert.True((await service.ValidateAsync(run.Value.Id)).Succeeded);
        Assert.True((await service.SubmitForApprovalAsync(run.Value.Id)).Succeeded);
        scenario.Tenant.UserId = scenario.CheckerId;
        Assert.True((await service.ApproveAsync(run.Value.Id)).Succeeded);
        var firstAttempt = await service.SubmitAsync(run.Value.Id);
        Assert.True(firstAttempt.Succeeded, firstAttempt.Message);
        Assert.Equal(StatutoryFilingRunStatus.Failed, firstAttempt.Value!.Status);
        var accepted = await service.UpdateConnectionProfileAsync(profile.Value.Id, new StatutoryFilingConnectionProfileRequest { Name = "Failure injection connector", ConnectorType = StatutoryFilingConnectorType.Test, Endpoint = "test://failure", NonSecretConfigurationJson = "{\"outcome\":\"accepted\"}", EffectiveFrom = new(2026, 1, 1) });
        Assert.True(accepted.Succeeded, accepted.Message);
        var retry = await service.SubmitAsync(run.Value.Id);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(StatutoryFilingRunStatus.Submitted, retry.Value!.Status);
        Assert.Equal(package.Value!.PackageHash, (await service.GetPackageAsync(run.Value.Id)).Value!.PackageHash);
        Assert.Single(await scenario.Db.StatutoryFilingSubmissions.Where(x => x.RunId == run.Value.Id).ToListAsync());
        var submission = await scenario.Db.StatutoryFilingSubmissions.SingleAsync(x => x.RunId == run.Value.Id);
        Assert.Equal("RETRYABLE_EXTERNAL_FAILURE", await scenario.Db.StatutoryFilingSubmissionAttempts.Where(x => x.SubmissionId == submission.Id).OrderBy(x => x.AttemptNumber).Select(x => x.ResponseCode).FirstAsync());
        Assert.Equal(2, await scenario.Db.StatutoryFilingSubmissionAttempts.CountAsync(x => x.SubmissionId == submission.Id));
        Assert.Equal(1, await scenario.Db.StatutoryFilingSubmissions.CountAsync(x => x.RunId == run.Value.Id && x.ExternalReference != null));
    }

    [Fact]
    public async Task Concurrent_conflict_retry_is_safe()
    {
        await new PayrollAdjustmentsConcurrencyTests().Concurrent_submit_has_one_authoritative_transition_and_history_effect();
    }

    private static PayrollAdjustmentRequest Request(Guid employeeId, Guid sourceId) => new()
    {
        EmployeeId = employeeId, EffectiveDate = new(2026, 9, 30), Description = "Failure injection adjustment",
        Amount = 100m, Direction = PayrollAdjustmentDirection.Earning, ComponentCode = "FAILURE", SourceType = "FailureInjection", SourceReferenceId = sourceId
    };

    private static async Task<Guid> SeedApprovedRunAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid maker, Guid checker)
    {
        var runId = Guid.NewGuid(); var periodId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"FF{tenantId:N}"[..10], TenantName = "Finalization failure", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        db.Users.AddRange(new User { Id = maker, TenantId = tenantId, Email = $"{maker:N}@test.local", PasswordHash = "test", IsActive = true }, new User { Id = checker, TenantId = tenantId, Email = $"{checker:N}@test.local", PasswordHash = "test", IsActive = true });
        db.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, RequireMakerChecker = true, PreventSelfApproval = true, UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = maker });
        db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "FF-26", Name = "Failure period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), Status = PayrollPeriodStatus.Locked });
        db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "FF-RUN", Status = PayrollRunStatus.Approved, StartedByUserId = maker, EmployeeCount = 0 });
        await db.SaveChangesAsync();
        return runId;
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId)
    {
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"FI{tenantId:N}"[..10], TenantName = "Failure injection", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "FI-001", FirstName = "Failure", LastName = "Injection", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();
    }

    private sealed class ThrowBeforeFinalizationCommitInterceptor : SaveChangesInterceptor
    {
        private bool thrown;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!thrown && eventData.Context is not null && eventData.Context.ChangeTracker.Entries<PayrollRun>().Any(x => x.State == EntityState.Modified && x.CurrentValues[nameof(PayrollRun.Status)] is PayrollRunStatus.Finalized))
            {
                thrown = true;
                throw new InvalidOperationException("Injected finalization failure before commit.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
