using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRMS.Tests;

public sealed class SeparationExitFailureInjectionTests
{
    [Fact]
    public async Task Employment_exit_failure_rolls_back_or_remains_retryable()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); var injector = new TestExitFailureInjector { Employment = true };
        await using (var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) { var result = await Service(db, f, injector).ExecuteAsync(f.SeparationId, new()); Assert.False(result.Succeeded); }
        await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); var retry = await Service(retryDb, f).RetryAsync(f.SeparationId, new("retry")); Assert.True(retry.Succeeded, retry.Message);
    }

    [Fact]
    public async Task Access_deprovision_failure_resumes_without_duplicate_employment_exit()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); var injector = new TestExitFailureInjector { Access = true };
        await using (var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) Assert.False((await Service(db, f, injector).ExecuteAsync(f.SeparationId, new())).Succeeded);
        await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.True((await Service(retryDb, f).RetryAsync(f.SeparationId, new("retry"))).Succeeded); Assert.Equal(1, await retryDb.EmployeeEmploymentHistory.CountAsync(x => x.EmployeeId == f.EmployeeId && x.EmploymentStatus == HRMS.Domain.Enums.EmployeeStatus.Terminated));
    }

    [Fact]
    public async Task Session_revocation_failure_is_retry_safe()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); var injector = new TestExitFailureInjector { Sessions = true };
        await using (var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) Assert.False((await Service(db, f, injector).ExecuteAsync(f.SeparationId, new())).Succeeded);
        await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.True((await Service(retryDb, f).RetryAsync(f.SeparationId, new("retry"))).Succeeded);
    }

    [Fact]
    public async Task Role_reconciliation_failure_is_retry_safe()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); var injector = new TestExitFailureInjector { Roles = true };
        await using (var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) Assert.False((await Service(db, f, injector).ExecuteAsync(f.SeparationId, new())).Succeeded);
        await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.True((await Service(retryDb, f).RetryAsync(f.SeparationId, new("retry"))).Succeeded);
    }

    [Fact]
    public async Task Final_closure_persistence_failure_reconciles_without_duplicate_side_effects()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); var injector = new TestExitFailureInjector { FinalClosure = true };
        await using (var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) Assert.False((await Service(db, f, injector).ExecuteAsync(f.SeparationId, new())).Succeeded);
        await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.True((await Service(retryDb, f).RetryAsync(f.SeparationId, new("retry"))).Succeeded); Assert.Equal(1, await retryDb.SeparationExitExecutionEvents.CountAsync(x => x.EventType == HRMS.Domain.Entities.Separation.SeparationExitExecutionEventType.SeparationClosed));
    }

    private static SeparationExitService Service(HrmsDbContext db, SeparationExitExecutionTests.ExitFixture f, TestExitFailureInjector? injector = null) => new(db, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System, failureInjector: injector);
}

internal sealed class TestExitFailureInjector : ISeparationExitFailureInjector
{
    public bool Employment { get; init; }
    public bool Access { get; init; }
    public bool Sessions { get; init; }
    public bool Roles { get; init; }
    public bool FinalClosure { get; init; }
    public void BeforeEmploymentExit() { if (Employment) throw new InvalidOperationException("Injected employment failure."); }
    public void BeforeAccessDeprovision() { if (Access) throw new InvalidOperationException("Injected access failure."); }
    public void BeforeSessionRevocation() { if (Sessions) throw new InvalidOperationException("Injected session failure."); }
    public void BeforeRoleReconciliation() { if (Roles) throw new InvalidOperationException("Injected role failure."); }
    public void BeforeFinalClosure() { if (FinalClosure) throw new InvalidOperationException("Injected closure failure."); }
}
