using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollProductionClosureTests
{
    [Fact]
    public async Task Production_health_classifies_missing_tenant_setup_as_warning_or_critical()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"P{tenantId:N}"[..12], TenantName = "Production", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var result = await new PayrollOperationsService(db, new TestTenantContext(tenantId)).GetProductionHealthAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Status, new[] { "Warning", "Critical" });
        Assert.NotEmpty(result.Value.Issues);
    }

    [Fact]
    public async Task Integrity_scan_is_tenant_scoped_and_healthy_for_empty_tenant()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"I{tenantId:N}"[..12], TenantName = "Integrity", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var result = await new PayrollOperationsService(db, new TestTenantContext(tenantId)).GetIntegrityAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Healthy", result.Value!.Status);
        Assert.All(result.Value.Checks, check => Assert.Equal(0, check.Count));
    }
}
