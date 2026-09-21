using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationBenefitsLargeDataTests
{
    [Fact]
    public async Task One_hundred_separation_calculations_reconcile_and_remain_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var otherTenantId = Guid.NewGuid(); var tenant = new TestTenantContext(tenantId, Guid.NewGuid());
        using (var seed = database.CreateContext(tenant))
        {
            seed.Tenants.AddRange(new Tenant { Id = tenantId, TenantCode = $"L7P{tenantId:N}"[..12], TenantName = "Large", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active }, new Tenant { Id = otherTenantId, TenantCode = $"L7Q{otherTenantId:N}"[..12], TenantName = "Other", Host = $"{otherTenantId:N}.test", ShardKey = otherTenantId.ToString("N"), Status = TenantStatus.Active });
            for (var i = 0; i < 100; i++) seed.Employees.Add(new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"L{i:000}", FirstName = "Large", LastName = $"Employee {i}", Email = $"large{i}@test.local", DateOfJoining = new(2023, 1, 1), Status = EmployeeStatus.Active });
            await seed.SaveChangesAsync();
        }
        using var db = database.CreateContext(tenant); var service = new SeparationBenefitsService(db, tenant, TimeProvider.System); var policy = await service.CreatePolicyAsync(new GratuityPolicyRequest { Code = "LARGE", Name = "Large data" }); Assert.True((await service.AddVersionAsync(policy.Value!.Id, new GratuityPolicyVersionRequest { EffectiveFrom = new(2026, 1, 1), MinimumServiceMonths = 1, FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 1000 })).Succeeded);
        var finalService = new PayrollRetroSettlementService(db, tenant, TimeProvider.System, separationBenefits: service); var employees = await db.Employees.Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync();
        foreach (var employeeId in employees)
        {
            var settlement = await finalService.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeId, SeparationDate = new(2026, 9, 1), LastWorkingDate = new(2026, 9, 1), SettlementDate = new(2026, 9, 1), SeparationReason = SeparationReason.Resignation }); Assert.True(settlement.Succeeded, settlement.Message); var calculated = await finalService.CalculateSettlementAsync(settlement.Value!.Id); Assert.True(calculated.Succeeded, calculated.Message);
        }
        Assert.Equal(100, await db.GratuityCalculations.CountAsync()); Assert.Equal(100000m, await db.GratuityCalculations.SumAsync(x => x.FinalGratuityAmount)); tenant.TenantId = otherTenantId; Assert.Empty(await db.GratuityCalculations.ToListAsync());
    }
}
