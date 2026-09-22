using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollAnalyticsTests
{
    [Fact]
    public async Task Reconciliation_is_persisted_and_tenant_scoped_without_mutating_payroll()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var otherTenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId); await SeedTenant(database, otherTenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var period = new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "AN-2026-09", Name = "Analytics", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open };
        var run = new PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = period.Id, RunNumber = "AN-RUN-1", Status = PayrollRunStatus.Calculated };
        db.PayrollPeriods.Add(period); db.PayrollRuns.Add(run); await db.SaveChangesAsync();
        var service = new PayrollAnalyticsService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var control = await service.CreateControlAsync(new PayrollAnalyticsControlRequest { Code = "GROSS_LIMIT", Name = "Gross limit", Metric = PayrollControlMetric.GrossPay, Scope = PayrollControlScope.PayrollRun, AbsoluteThreshold = 1m, EffectiveFrom = new(2026, 1, 1) });
        Assert.True(control.Succeeded, control.Message);
        var generated = await service.GenerateReconciliationAsync(run.Id, PayrollReconciliationType.PrePayroll);
        Assert.True(generated.Succeeded, generated.Message); Assert.Equal(PayrollReconciliationStatus.Generated, generated.Value!.Status); Assert.Equal(2, generated.Value.TotalChecks); Assert.Single(await db.PayrollAnalyticsSnapshots.ToListAsync());
        var other = await new PayrollAnalyticsService(db, new TestTenantContext(otherTenantId), TimeProvider.System).GetOverviewAsync(run.Id);
        Assert.False(other.Succeeded); Assert.Equal(PayrollRunStatus.Calculated, (await db.PayrollRuns.FindAsync(run.Id))!.Status);
    }

    private static async Task SeedTenant(SqliteInMemoryDatabase database, Guid id)
    { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = id, TenantCode = $"AN{id:N}"[..12], TenantName = "Analytics", Host = $"{id:N}.test", ShardKey = id.ToString("N"), Status = TenantStatus.Active }); await db.SaveChangesAsync(); }
}
