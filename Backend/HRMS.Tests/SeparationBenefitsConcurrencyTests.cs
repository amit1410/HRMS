using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationBenefitsConcurrencyTests
{
    [Fact]
    public async Task Calculate_and_finalize_retries_produce_one_snapshot_and_one_effect()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"C7P{tenantId:N}"[..12], TenantName = "Concurrency", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"C7P{employeeId:N}"[..12], FirstName = "Concurrency", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2023, 1, 1), Status = EmployeeStatus.Active });
            await seed.SaveChangesAsync();
        }
        var tenant = new TestTenantContext(tenantId, Guid.NewGuid()); using var db = database.CreateContext(tenant); var benefits = new SeparationBenefitsService(db, tenant, TimeProvider.System);
        var policy = await benefits.CreatePolicyAsync(new GratuityPolicyRequest { Code = "CONCURRENT", Name = "Concurrent" }); Assert.True((await benefits.AddVersionAsync(policy.Value!.Id, new GratuityPolicyVersionRequest { EffectiveFrom = new(2026, 1, 1), MinimumServiceMonths = 1, FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 9000 })).Succeeded);
        var settlementService = new PayrollRetroSettlementService(db, tenant, TimeProvider.System, separationBenefits: benefits); var settlement = await settlementService.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeId, SeparationDate = new(2026, 9, 1), LastWorkingDate = new(2026, 9, 1), SettlementDate = new(2026, 9, 1), SeparationReason = SeparationReason.Resignation }); Assert.True(settlement.Succeeded);
        var request = new SeparationBenefitCalculationRequest { FinalSettlementId = settlement.Value!.Id, SeparationDate = new(2026, 9, 1), LastWorkingDate = new(2026, 9, 1), SeparationReason = SeparationReason.Resignation };
        var first = await benefits.CalculateAsync(employeeId, settlement.Value.Id, request); var replay = await benefits.CalculateAsync(employeeId, settlement.Value.Id, request);
        Assert.True(first.Succeeded, first.Message); Assert.True(replay.Succeeded, replay.Message); Assert.Single(await db.GratuityCalculations.Where(x => x.FinalSettlementId == settlement.Value.Id).ToListAsync());
        Assert.True((await settlementService.CalculateSettlementAsync(settlement.Value.Id)).Succeeded); Assert.True((await settlementService.ApproveSettlementAsync(settlement.Value.Id)).Succeeded); Assert.True((await settlementService.FinalizeSettlementAsync(settlement.Value.Id)).Succeeded); Assert.False((await settlementService.FinalizeSettlementAsync(settlement.Value.Id)).Succeeded);
        Assert.Single(await db.FinalSettlementLines.Where(x => x.FinalSettlementCaseId == settlement.Value.Id && x.SourceType == "Gratuity").ToListAsync());
    }
}
