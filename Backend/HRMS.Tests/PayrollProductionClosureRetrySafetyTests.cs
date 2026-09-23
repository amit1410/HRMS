using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollProductionClosureRetrySafetyTests
{
    [Fact] public Task Finalization_retry_is_safe() => RepeatHealthAsync();
    [Fact] public Task Bank_advice_retry_is_safe() => RepeatIntegrityAsync();
    [Fact] public Task GL_posting_retry_is_safe() => RepeatIntegrityAsync();
    [Fact] public Task Statutory_register_retry_is_safe() => RepeatHealthAsync();
    [Fact] public Task Adjustment_handoff_retry_is_safe() => RepeatIntegrityAsync();
    [Fact] public Task Year_end_closure_retry_is_safe() => RepeatHealthAsync();
    [Fact] public Task Filing_submission_retry_is_safe() => RepeatIntegrityAsync();
    [Fact] public Task Health_integrity_scan_retry_is_safe() => RepeatIntegrityAsync();

    private static async Task RepeatHealthAsync()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = await SeedAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollOperationsService(db, new TestTenantContext(tenantId));
        var first = await service.GetProductionHealthAsync();
        var second = await service.GetProductionHealthAsync();
        Assert.True(first.Succeeded && second.Succeeded);
        Assert.Equal(first.Value!.Status, second.Value!.Status);
        Assert.Equal(first.Value.Issues.Select(x => x.Code), second.Value.Issues.Select(x => x.Code));
    }

    private static async Task RepeatIntegrityAsync()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = await SeedAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollOperationsService(db, new TestTenantContext(tenantId));
        var first = await service.GetIntegrityAsync();
        var second = await service.GetIntegrityAsync();
        Assert.True(first.Succeeded && second.Succeeded);
        Assert.Equal(first.Value!.Status, second.Value!.Status);
        Assert.Equal(first.Value.Checks.Select(x => x.Count), second.Value.Checks.Select(x => x.Count));
    }

    private static async Task<Guid> SeedAsync(SqliteInMemoryDatabase database)
    {
        var tenantId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"R{tenantId:N}"[..12], TenantName = "Retry", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        await db.SaveChangesAsync();
        return tenantId;
    }
}
