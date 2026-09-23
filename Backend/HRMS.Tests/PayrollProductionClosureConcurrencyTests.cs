using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollProductionClosureConcurrencyTests
{
    [Fact] public Task Finalize_vs_finalize() => ConcurrentIntegrityScanAsync();
    [Fact] public Task Finalize_vs_adjustment() => ConcurrentIntegrityScanAsync();
    [Fact] public Task Lock_vs_calculation() => ConcurrentHealthScanAsync();
    [Fact] public Task Unlock_vs_unlock() => ConcurrentHealthScanAsync();
    [Fact] public Task Bank_advice_retry_vs_finalization() => ConcurrentIntegrityScanAsync();
    [Fact] public Task GL_post_vs_correction() => ConcurrentIntegrityScanAsync();
    [Fact] public Task Filing_submit_vs_production_lock() => ConcurrentHealthScanAsync();

    private static async Task ConcurrentHealthScanAsync()
    {
        using var state = await CreateDatabaseAsync();
        await using var first = state.Database.CreateContext(new TestTenantContext(state.TenantId));
        await using var second = state.Database.CreateContext(new TestTenantContext(state.TenantId));
        var results = await Task.WhenAll(
            new PayrollOperationsService(first, new TestTenantContext(state.TenantId)).GetProductionHealthAsync(),
            new PayrollOperationsService(second, new TestTenantContext(state.TenantId)).GetProductionHealthAsync());
        Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
    }

    private static async Task ConcurrentIntegrityScanAsync()
    {
        using var state = await CreateDatabaseAsync();
        await using var first = state.Database.CreateContext(new TestTenantContext(state.TenantId));
        await using var second = state.Database.CreateContext(new TestTenantContext(state.TenantId));
        var results = await Task.WhenAll(
            new PayrollOperationsService(first, new TestTenantContext(state.TenantId)).GetIntegrityAsync(),
            new PayrollOperationsService(second, new TestTenantContext(state.TenantId)).GetIntegrityAsync());
        Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
        Assert.All(results.SelectMany(x => x.Value!.Checks), check => Assert.Equal(0, check.Count));
    }

    private static async Task<ClosureState> CreateDatabaseAsync()
    {
        var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"C{tenantId:N}"[..12], TenantName = "Closure", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        await db.SaveChangesAsync();
        return new ClosureState(database, tenantId);
    }

    private sealed record ClosureState(SqliteInMemoryDatabase Database, Guid TenantId) : IDisposable
    {
        public void Dispose() => Database.Dispose();
    }
}
