using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationBenefitsFoundationTests
{
    [Fact]
    public async Task Effective_policy_calculates_configured_formula_and_tax_split()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database);
        using var db = database.CreateContext(new TestTenantContext(ids.Tenant, Guid.NewGuid()));
        var service = new SeparationBenefitsService(db, new TestTenantContext(ids.Tenant, Guid.NewGuid()), TimeProvider.System);
        var policy = await service.CreatePolicyAsync(new GratuityPolicyRequest { Code = "STANDARD", Name = "Standard gratuity" });
        Assert.True(policy.Succeeded, policy.Message);
        var version = await service.AddVersionAsync(policy.Value!.Id, new GratuityPolicyVersionRequest
        {
            EffectiveFrom = new(2026, 1, 1), MinimumServiceMonths = 12,
            FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 12500,
            MaximumBenefit = 10000, TaxTreatment = SeparationBenefitTaxTreatment.PartiallyTaxable,
            TaxablePercentage = 25
        });
        Assert.True(version.Succeeded, version.Message);

        var result = await service.PreviewAsync(ids.Employee, new SeparationBenefitCalculationRequest
        {
            SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30),
            SeparationReason = SeparationReason.Resignation
        });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(10000m, result.Value!.Gratuity!.FinalGratuityAmount);
        Assert.Equal(2500m, result.Value.Gratuity.TaxableAmount);
        Assert.Equal(7500m, result.Value.Gratuity.NonTaxableAmount);
        Assert.True(result.Value.Gratuity.CapApplied);
    }

    [Fact]
    public async Task Overlapping_published_versions_are_rejected_and_tenant_scope_is_enforced()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database);
        var tenant = new TestTenantContext(ids.Tenant, Guid.NewGuid());
        using var db = database.CreateContext(tenant);
        var service = new SeparationBenefitsService(db, tenant, TimeProvider.System);
        var policy = await service.CreatePolicyAsync(new GratuityPolicyRequest { Code = "OVERLAP", Name = "Overlap" });
        Assert.True(policy.Succeeded);
        Assert.True((await service.AddVersionAsync(policy.Value!.Id, new GratuityPolicyVersionRequest { EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 1 })).Succeeded);
        var overlap = await service.AddVersionAsync(policy.Value.Id, new GratuityPolicyVersionRequest { EffectiveFrom = new(2026, 6, 1), FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 2 });
        Assert.False(overlap.Succeeded);

        tenant.TenantId = Guid.NewGuid();
        var otherTenantPolicies = await service.GetPoliciesAsync();
        Assert.True(otherTenantPolicies.Succeeded);
        Assert.Empty(otherTenantPolicies.Value!);
    }

    [Fact]
    public async Task Calculation_snapshot_is_persisted_and_finalization_is_idempotent()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database);
        var tenant = new TestTenantContext(ids.Tenant, Guid.NewGuid());
        using var db = database.CreateContext(tenant);
        var policyService = new SeparationBenefitsService(db, tenant, TimeProvider.System);
        var policy = await policyService.CreatePolicyAsync(new GratuityPolicyRequest { Code = "FINAL", Name = "Final settlement policy" });
        Assert.True((await policyService.AddVersionAsync(policy.Value!.Id, new GratuityPolicyVersionRequest { EffectiveFrom = new(2026, 1, 1), MinimumServiceMonths = 1, FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 5000 })).Succeeded);
        var finalService = new PayrollRetroSettlementService(db, tenant, TimeProvider.System, separationBenefits: policyService);
        var settlement = await finalService.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = ids.Employee, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 9, 30), CurrencyCode = "INR", SeparationReason = SeparationReason.Resignation });
        Assert.True(settlement.Succeeded, settlement.Message);
        Assert.True((await finalService.CalculateSettlementAsync(settlement.Value!.Id)).Succeeded);
        Assert.Single(await db.GratuityCalculations.Where(x => x.FinalSettlementId == settlement.Value.Id).ToListAsync());
        Assert.Single(await db.FinalSettlementLines.Where(x => x.FinalSettlementCaseId == settlement.Value.Id && x.SourceType == "Gratuity").ToListAsync());
        Assert.True((await finalService.ApproveSettlementAsync(settlement.Value.Id)).Succeeded);
        Assert.True((await finalService.FinalizeSettlementAsync(settlement.Value.Id)).Succeeded);
        Assert.False((await finalService.FinalizeSettlementAsync(settlement.Value.Id)).Succeeded);
        Assert.Equal(SeparationBenefitCalculationStatus.Finalized, await db.GratuityCalculations.Select(x => x.Status).SingleAsync());
    }

    private static async Task<(Guid Tenant, Guid Employee)> SeedAsync(SqliteInMemoryDatabase database)
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"P7P{tenantId:N}"[..12], TenantName = "Phase 7P", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"P7P{employeeId:N}"[..12], FirstName = "Separation", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();
        return (tenantId, employeeId);
    }
}
