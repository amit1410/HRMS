using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollControlsTests
{
    [Fact]
    public async Task Lock_requires_reason_and_unlock_persists_audit_metadata()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollPeriodService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var created = await service.CreateAsync(Period("CTRL-26"));
        Assert.True(created.Succeeded, created.Message);
        var opened = await service.TransitionAsync(created.Value!.Id, "open", created.Value.ConcurrencyVersion);
        var closed = await service.TransitionAsync(created.Value.Id, "close", opened.Value!.ConcurrencyVersion);

        var missingReason = await service.TransitionAsync(created.Value.Id, "lock", closed.Value!.ConcurrencyVersion);
        Assert.Equal(ResultStatus.ValidationFailed, missingReason.Status);
        var locked = await service.TransitionAsync(created.Value.Id, "lock", closed.Value.ConcurrencyVersion, "period review complete");
        Assert.True(locked.Succeeded, locked.Message);
        var persisted = await db.PayrollPeriods.FindAsync(created.Value.Id);
        Assert.Equal("period review complete", persisted!.LockReason);
        Assert.NotNull(persisted.LockedAtUtc);

        var unlockMissingReason = await service.TransitionAsync(created.Value.Id, "unlock", locked.Value!.ConcurrencyVersion);
        Assert.Equal(ResultStatus.ValidationFailed, unlockMissingReason.Status);
        var unlocked = await service.TransitionAsync(created.Value.Id, "unlock", locked.Value.ConcurrencyVersion, "correction window approved");
        Assert.True(unlocked.Succeeded, unlocked.Message);
        Assert.Equal(PayrollPeriodStatus.Closed, unlocked.Value!.Status);
        Assert.Equal("correction window approved", (await db.PayrollPeriods.FindAsync(created.Value.Id))!.UnlockReason);
    }

    [Fact]
    public async Task Locked_period_with_approved_run_cannot_be_unlocked()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollPeriodService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var created = await service.CreateAsync(Period("CTRL-27"));
        var opened = await service.TransitionAsync(created.Value!.Id, "open", created.Value.ConcurrencyVersion);
        var closed = await service.TransitionAsync(created.Value.Id, "close", opened.Value!.ConcurrencyVersion);
        var locked = await service.TransitionAsync(created.Value.Id, "lock", closed.Value!.ConcurrencyVersion, "period review complete");
        db.PayrollRuns.Add(new PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = created.Value.Id, RunNumber = "RUN-CTRL-27", Status = PayrollRunStatus.Approved });
        await db.SaveChangesAsync();

        var result = await service.TransitionAsync(created.Value.Id, "unlock", locked.Value!.ConcurrencyVersion, "urgent correction");
        Assert.Equal(ResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Approval_guard_blocks_self_approval_when_policy_requires_a_checker()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId, userId));
        db.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, UpdatedAtUtc = DateTime.UtcNow, RequireMakerChecker = true, PreventSelfApproval = true });
        await db.SaveChangesAsync();
        var guard = new PayrollApprovalGuard(db, new TestTenantContext(tenantId, userId));
        Assert.Equal(ResultStatus.ValidationFailed, (await guard.ValidateAsync(userId, "approve")).Status);
        Assert.True((await guard.ValidateAsync(Guid.NewGuid(), "approve")).Succeeded);
    }

    [Fact]
    public async Task Payroll_run_approval_uses_started_by_as_maker()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var maker = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId, maker));
        db.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, UpdatedAtUtc = DateTime.UtcNow, RequireMakerChecker = true, PreventSelfApproval = true });
        var period = new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "RUN-CTRL", Name = "Run Controls", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open };
        db.PayrollPeriods.Add(period);
        db.PayrollRuns.Add(new PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = period.Id, RunNumber = "RUN-CTRL-1", Status = PayrollRunStatus.Calculated, StartedByUserId = maker });
        await db.SaveChangesAsync();
        var run = await db.PayrollRuns.SingleAsync();
        var result = await new PayrollRunService(db, new TestTenantContext(tenantId, maker), TimeProvider.System).TransitionAsync(run.Id, PayrollRunStatus.Approved);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Readiness_reports_a_blocking_error_for_a_run_without_eligible_employees()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var context = new TestTenantContext(tenantId);
        var period = await new PayrollPeriodService(db, context, TimeProvider.System).CreateAsync(Period("READY-26"));
        var run = await new PayrollRunService(db, context, TimeProvider.System).CreateAsync(new PayrollRunRequest { PayrollPeriodId = period.Value!.Id });
        var readiness = await new PayrollReadinessService(db, context).CheckAsync(run.Value!.Id);
        Assert.False(readiness.Value!.Ready);
        Assert.Contains(readiness.Value.Checks, x => x.Code == "NoEligibleEmployees" && x.Blocking);
    }

    [Fact]
    public async Task Configuration_health_is_tenant_scoped_and_reports_missing_controls()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var health = await new PayrollOperationsService(db, new TestTenantContext(tenantId)).GetConfigurationHealthAsync();
        Assert.True(health.Succeeded, health.Message);
        Assert.Contains(health.Value!.Categories, x => x.Category == "Controls" && x.Status == "Warning");
        Assert.Contains(health.Value.Categories, x => x.Category == "Salary");
    }

    [Fact]
    public async Task Operations_dashboard_returns_tenant_scoped_lifecycle_counts()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.PayrollPeriods.Add(new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "OPS-OPEN", Name = "Operations", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open });
        await db.SaveChangesAsync();
        var dashboard = await new PayrollOperationsService(db, new TestTenantContext(tenantId)).GetOperationsDashboardAsync();
        Assert.True(dashboard.Succeeded, dashboard.Message);
        Assert.Equal(1, dashboard.Value!.OpenPeriods);
        Assert.Equal(0, dashboard.Value.LockedPeriods);
    }

    private static PayrollPeriodRequest Period(string code) => new() { Code = code, Name = code, PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9 };

    private static async Task SeedTenant(SqliteInMemoryDatabase database, Guid id)
    {
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = id, TenantCode = $"T{id:N}"[..12], TenantName = "Controls", Host = $"{id:N}.test", ShardKey = id.ToString("N") });
        await db.SaveChangesAsync();
    }
}
