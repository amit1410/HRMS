using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Domain.Entities;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SalaryComponentMasterTests
{
    [Fact]
    public async Task Creates_component_and_audit_history_with_normalized_code()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new SalaryComponentService(db, new TestTenantContext(tenantId), TimeProvider.System);

        var code = $"BASIC_{Guid.NewGuid():N}";
        var result = await service.CreateAsync(Request($" {code} ", "Basic Salary"));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(code.ToUpperInvariant(), result.Value!.Code);
        Assert.Single(db.SalaryComponentHistories);
    }

    [Fact]
    public async Task Duplicate_code_is_rejected_within_tenant_but_allowed_across_tenants()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var code = $"BASIC_{Guid.NewGuid():N}";
        await SeedTenantAsync(database, tenantA);
        await SeedTenantAsync(database, tenantB);
        await using (var db = database.CreateContext(new TestTenantContext(tenantA)))
        {
            var service = new SalaryComponentService(db, new TestTenantContext(tenantA), TimeProvider.System);
            Assert.True((await service.CreateAsync(Request(code, "Basic"))).Succeeded);
            Assert.Equal(ResultStatus.Conflict, (await service.CreateAsync(Request(code.ToLowerInvariant(), "Duplicate"))).Status);
        }
        await using var otherDb = database.CreateContext(new TestTenantContext(tenantB));
        var other = new SalaryComponentService(otherDb, new TestTenantContext(tenantB), TimeProvider.System);
        var otherResult = await other.CreateAsync(Request(code, "Basic"));
        Assert.True(otherResult.Succeeded, otherResult.Message);
    }

    [Fact]
    public async Task Statutory_and_date_rules_are_validated()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        await SeedTenantAsync(database, tenant.TenantId!.Value);
        await using var db = database.CreateContext(tenant);
        var service = new SalaryComponentService(db, tenant, TimeProvider.System);

        var invalidStatutory = Request("PF", "PF");
        invalidStatutory.IsStatutory = true;
        var invalidDates = Request("DATE", "Date");
        invalidDates.EffectiveFrom = new(2027, 1, 1);
        invalidDates.EffectiveTo = new(2026, 12, 31);

        Assert.Equal(ResultStatus.ValidationFailed, (await service.CreateAsync(invalidStatutory)).Status);
        Assert.Equal(ResultStatus.ValidationFailed, (await service.CreateAsync(invalidDates)).Status);
    }

    [Fact]
    public async Task Deactivation_preserves_component_and_history()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        await SeedTenantAsync(database, tenant.TenantId!.Value);
        await using var db = database.CreateContext(tenant);
        var service = new SalaryComponentService(db, tenant, TimeProvider.System);
        var created = await service.CreateAsync(Request($"HRA_{Guid.NewGuid():N}", "House Rent Allowance"));

        var result = await service.SetActiveAsync(created.Value!.Id, false, created.Value.ConcurrencyVersion);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsActive);
        Assert.Equal(2, db.SalaryComponentHistories.Count());
    }

    [Fact]
    public async Task Tenant_cannot_read_update_toggle_or_read_history_for_another_tenant_component()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await SeedTenantAsync(database, tenantA);
        await SeedTenantAsync(database, tenantB);

        Guid componentId;
        await using (var tenantBDb = database.CreateContext(new TestTenantContext(tenantB)))
        {
            var tenantBService = new SalaryComponentService(tenantBDb, new TestTenantContext(tenantB), TimeProvider.System);
            componentId = (await tenantBService.CreateAsync(Request("BASIC", "Basic"))).Value!.Id;
        }

        await using var tenantADb = database.CreateContext(new TestTenantContext(tenantA));
        var tenantAService = new SalaryComponentService(tenantADb, new TestTenantContext(tenantA), TimeProvider.System);
        Assert.Equal(ResultStatus.NotFound, (await tenantAService.GetByIdAsync(componentId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await tenantAService.UpdateAsync(componentId, Request("CHANGED", "Changed"))).Status);
        Assert.Equal(ResultStatus.NotFound, (await tenantAService.SetActiveAsync(componentId, false, null)).Status);
        Assert.Equal(ResultStatus.NotFound, (await tenantAService.GetHistoryAsync(componentId)).Status);
    }

    private static SalaryComponentRequest Request(string code, string name) => new()
    {
        Code = code,
        Name = name,
        ComponentType = SalaryComponentType.Earning,
        CalculationType = SalaryCalculationType.FixedAmount,
        EffectiveFrom = new(2026, 1, 1),
        IsRecurring = true,
        AffectsGross = true,
        AffectsNetPay = true
    };

    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..Math.Min(20, tenantId.ToString("N").Length + 1)], Host = $"{tenantId:N}.test", ShardKey = $"shard-{tenantId:N}", TenantName = "Test Tenant" });
        await db.SaveChangesAsync();
    }
}
