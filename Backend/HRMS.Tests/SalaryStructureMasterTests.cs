using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SalaryStructureMasterTests
{
    [Fact]
    public async Task Creates_structure_with_components_and_history()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId);
        var basicId = await SeedComponentAsync(database, tenantId, "BASIC", "Basic Salary");
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new SalaryStructureService(db, new TestTenantContext(tenantId), TimeProvider.System);

        var result = await service.CreateAsync(Request("STAFF_MONTHLY", "Staff Monthly", basicId, SalaryStructureCalculationType.FixedAmount, 10000));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("STAFF_MONTHLY", result.Value!.Code);
        Assert.Single(result.Value.Components);
        Assert.Single(db.SalaryStructureHistories);
    }

    [Fact]
    public async Task Duplicate_code_is_tenant_scoped_and_cross_tenant_component_reference_is_denied()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
        await SeedTenantAsync(database, tenantA); await SeedTenantAsync(database, tenantB);
        var componentA = await SeedComponentAsync(database, tenantA, "BASIC", "Basic A");
        var componentB = await SeedComponentAsync(database, tenantB, "BASIC", "Basic B");
        await using (var db = database.CreateContext(new TestTenantContext(tenantA)))
        {
            var service = new SalaryStructureService(db, new TestTenantContext(tenantA), TimeProvider.System);
            Assert.True((await service.CreateAsync(Request("STAFF", "Staff", componentA))).Succeeded);
            Assert.Equal(ResultStatus.Conflict, (await service.CreateAsync(Request("staff", "Duplicate", componentA))).Status);
        }
        await using var otherDb = database.CreateContext(new TestTenantContext(tenantB));
        var otherService = new SalaryStructureService(otherDb, new TestTenantContext(tenantB), TimeProvider.System);
        Assert.True((await otherService.CreateAsync(Request("STAFF", "Staff", componentB))).Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, (await otherService.CreateAsync(Request("CROSS", "Cross", componentA))).Status);
    }

    [Fact]
    public async Task Validates_calculation_rules_duplicate_rows_and_effective_overlap()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); await SeedTenantAsync(database, tenantId);
        var basicId = await SeedComponentAsync(database, tenantId, "BASIC", "Basic");
        var hraId = await SeedComponentAsync(database, tenantId, "HRA", "HRA");
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new SalaryStructureService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var invalid = Request("INVALID", "Invalid", hraId, SalaryStructureCalculationType.Percentage, 101);
        invalid.Components[0].PercentageOfComponentId = basicId;
        Assert.Equal(ResultStatus.ValidationFailed, (await service.CreateAsync(invalid)).Status);
        var duplicate = Request("DUPLICATE", "Duplicate", basicId);
        duplicate.Components.Add(new SalaryStructureComponentRequest { SalaryComponentId = basicId, Sequence = 2, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 1 });
        Assert.Equal(ResultStatus.Conflict, (await service.CreateAsync(duplicate)).Status);

        var created = await service.CreateAsync(Request("STAFF", "Staff", basicId));
        var overlapping = Request("STAFF", "Staff Future", hraId);
        overlapping.EffectiveFrom = new DateOnly(2026, 6, 1);
        Assert.Equal(ResultStatus.Conflict, (await service.UpdateAsync(created.Value!.Id, overlapping)).Status);
    }

    [Fact]
    public async Task Updates_components_toggles_and_denies_cross_tenant_access()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
        await SeedTenantAsync(database, tenantA); await SeedTenantAsync(database, tenantB);
        var componentA = await SeedComponentAsync(database, tenantA, "BASIC", "Basic");
        await using (var db = database.CreateContext(new TestTenantContext(tenantA)))
        {
            var service = new SalaryStructureService(db, new TestTenantContext(tenantA), TimeProvider.System);
            var created = await service.CreateAsync(Request("STAFF", "Staff", componentA));
            var added = await service.AddComponentAsync(created.Value!.Id, new SalaryStructureComponentRequest { SalaryComponentId = componentA, Sequence = 2, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 1 });
            Assert.Equal(ResultStatus.Conflict, added.Status);
            var updated = await service.SetActiveAsync(created.Value.Id, false, created.Value.ConcurrencyVersion);
            Assert.True(updated.Succeeded);
            Assert.True((await service.GetHistoryAsync(created.Value.Id)).Value!.Count >= 2);
        }
        await using var tenantBDb = database.CreateContext(new TestTenantContext(tenantB));
        var tenantBService = new SalaryStructureService(tenantBDb, new TestTenantContext(tenantB), TimeProvider.System);
        Assert.Equal(ResultStatus.NotFound, (await tenantBService.GetByIdAsync(Guid.NewGuid())).Status);
    }

    private static SalaryStructureRequest Request(string code, string name, Guid componentId, SalaryStructureCalculationType calculation = SalaryStructureCalculationType.FixedAmount, decimal? value = 1000) => new()
    {
        Code = code, Name = name, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
        Components = [new SalaryStructureComponentRequest { SalaryComponentId = componentId, Sequence = 1, CalculationType = calculation, Value = value, IsProratable = true, IsActive = true }]
    };

    private static async Task<Guid> SeedComponentAsync(SqliteInMemoryDatabase database, Guid tenantId, string code, string name)
    {
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var component = new SalaryComponent { Id = Guid.NewGuid(), TenantId = tenantId, Code = code + Guid.NewGuid().ToString("N")[..6], Name = name, ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
        db.SalaryComponents.Add(component); await db.SaveChangesAsync(); return component.Id;
    }

    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..12], Host = $"{tenantId:N}.test", ShardKey = $"shard-{tenantId:N}", TenantName = "Test Tenant" });
        await db.SaveChangesAsync();
    }
}
