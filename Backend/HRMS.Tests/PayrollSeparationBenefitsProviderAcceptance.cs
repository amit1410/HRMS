using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollSeparationBenefitsProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); tenant.TenantId = tenantId; tenant.UserId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"P7P{tenantId:N}"[..12], TenantName = "Separation provider", Host = $"{tenantId:N}.separation.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active, DatabaseProvider = provider });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"P7P-E{employeeId:N}"[..12], FirstName = "Provider", LastName = "Employee", Email = $"{employeeId:N}@separation.test", DateOfJoining = new DateOnly(2024, 1, 1), Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();

        var service = new SeparationBenefitsService(db, tenant, TimeProvider.System);
        var policy = await service.CreatePolicyAsync(new GratuityPolicyRequest { Code = "PROVIDER", Name = "Provider gratuity" }); Assert.True(policy.Succeeded, policy.Message);
        var version = await service.AddVersionAsync(policy.Value!.Id, new GratuityPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), MinimumServiceMonths = 12, FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 10000, TaxTreatment = SeparationBenefitTaxTreatment.PartiallyTaxable, TaxablePercentage = 20 }); Assert.True(version.Succeeded, version.Message);
        var finalService = new PayrollRetroSettlementService(db, tenant, TimeProvider.System, separationBenefits: service);
        var settlement = await finalService.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 9, 30), CurrencyCode = "INR", SeparationReason = SeparationReason.Resignation }); Assert.True(settlement.Succeeded, settlement.Message);
        var calculated = await finalService.CalculateSettlementAsync(settlement.Value!.Id); Assert.True(calculated.Succeeded, calculated.Message);
        var persisted = await db.GratuityCalculations.SingleAsync(x => x.TenantId == tenantId && x.FinalSettlementId == settlement.Value.Id); Assert.Equal(10000m, persisted.FinalGratuityAmount); Assert.Single(await db.FinalSettlementLines.Where(x => x.FinalSettlementCaseId == settlement.Value.Id && x.SourceType == "Gratuity").ToListAsync());
        Assert.True((await finalService.ApproveSettlementAsync(settlement.Value.Id)).Succeeded); Assert.True((await finalService.FinalizeSettlementAsync(settlement.Value.Id)).Succeeded); Assert.False((await finalService.FinalizeSettlementAsync(settlement.Value.Id)).Succeeded);
        Assert.Equal(SeparationBenefitCalculationStatus.Finalized, await db.GratuityCalculations.Where(x => x.Id == persisted.Id).Select(x => x.Status).SingleAsync());
        tenant.TenantId = Guid.NewGuid(); Assert.Empty(await db.GratuityCalculations.AsNoTracking().ToListAsync());
    }
}
