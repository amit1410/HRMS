using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class VariablePayFoundationTests
{
    [Fact]
    public async Task Published_fixed_plan_generates_numbered_award_and_is_idempotent()
    {
        using var database = new SqliteInMemoryDatabase(); var ids = await SeedAsync(database); var tenant = new TestTenantContext(ids.Tenant); using var db = database.CreateContext(tenant); var service = new VariablePayService(db, tenant, TimeProvider.System);
        var plan = await service.CreatePlanAsync(new VariablePayPlanRequest { Code = "BONUS", Name = "Annual bonus", PlanType = VariablePayPlanType.FixedBonus }); Assert.True(plan.Succeeded, plan.Message);
        var version = await service.AddVersionAsync(plan.Value!.Id, new VariablePayPlanVersionRequest { EffectiveFrom = new(2026, 1, 1), CalculationMethod = VariablePayCalculationMethod.FixedAmount, FixedAmount = 10000, TaxTreatment = VariablePayTaxTreatment.PartiallyTaxable, TaxablePercentage = 40 }); Assert.True(version.Succeeded, version.Message);
        var request = new VariablePayAwardRequest { PlanVersionId = version.Value!.Versions.Single().Id, AwardPeriodFrom = new(2026, 1, 1), AwardPeriodTo = new(2026, 12, 31), EligibilityDate = new(2026, 12, 31) };
        var first = await service.GenerateAsync(ids.Employee, request); var second = await service.GenerateAsync(ids.Employee, request); Assert.True(first.Succeeded, first.Message); Assert.True(second.Succeeded, second.Message); Assert.Equal("VPA/2026/000001", first.Value!.AwardNumber); Assert.Equal(first.Value.Id, second.Value!.Id); Assert.Equal(4000m, first.Value.TaxableAmount); Assert.Equal(6000m, first.Value.NonTaxableAmount);
    }

    [Fact]
    public async Task Overlapping_published_versions_are_rejected_and_tenant_isolation_holds()
    {
        using var database = new SqliteInMemoryDatabase(); var ids = await SeedAsync(database); var tenant = new TestTenantContext(ids.Tenant); using var db = database.CreateContext(tenant); var service = new VariablePayService(db, tenant, TimeProvider.System); var plan = await service.CreatePlanAsync(new VariablePayPlanRequest { Code = "OVERLAP", Name = "Overlap", PlanType = VariablePayPlanType.TargetVariablePay }); Assert.True(plan.Succeeded);
        Assert.True((await service.AddVersionAsync(plan.Value!.Id, new VariablePayPlanVersionRequest { EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), CalculationMethod = VariablePayCalculationMethod.TargetWithMultiplier, TargetPercentage = 10 })).Succeeded); var overlap = await service.AddVersionAsync(plan.Value.Id, new VariablePayPlanVersionRequest { EffectiveFrom = new(2026, 6, 1), CalculationMethod = VariablePayCalculationMethod.TargetWithMultiplier, TargetPercentage = 10 }); Assert.False(overlap.Succeeded); tenant.TenantId = Guid.NewGuid(); Assert.Empty((await service.GetPlansAsync()).Value!);
    }

    [Fact]
    public async Task Approval_requires_submit_and_settlement_cannot_exceed_outstanding()
    {
        using var database = new SqliteInMemoryDatabase(); var ids = await SeedAsync(database); var tenant = new TestTenantContext(ids.Tenant, Guid.NewGuid()); using var db = database.CreateContext(tenant); var service = new VariablePayService(db, tenant, TimeProvider.System); var plan = await service.CreatePlanAsync(new VariablePayPlanRequest { Code = "PAY", Name = "Pay", PlanType = VariablePayPlanType.OneTimeAward }); var version = await service.AddVersionAsync(plan.Value!.Id, new VariablePayPlanVersionRequest { EffectiveFrom = new(2026, 1, 1), CalculationMethod = VariablePayCalculationMethod.FixedAmount, FixedAmount = 1000 }); var award = await service.GenerateAsync(ids.Employee, new VariablePayAwardRequest { PlanVersionId = version.Value!.Versions.Single().Id, AwardPeriodFrom = new(2026, 2, 1), AwardPeriodTo = new(2026, 2, 28), EligibilityDate = new(2026, 2, 1) }); Assert.True((await service.SubmitAsync(award.Value!.Id)).Succeeded); tenant.UserId = Guid.NewGuid(); Assert.True((await service.ApproveAsync(award.Value.Id)).Succeeded); var partial = await service.SettleAsync(award.Value.Id, new VariablePaySettlementRequest { SettlementType = VariablePaySettlementType.Payroll, Amount = 400 }); Assert.True(partial.Succeeded, partial.Message); var over = await service.SettleAsync(award.Value.Id, new VariablePaySettlementRequest { SettlementType = VariablePaySettlementType.Payroll, Amount = 601 }); Assert.False(over.Succeeded); Assert.Equal(400m, partial.Value!.SettledAmount); Assert.Equal(600m, partial.Value.OutstandingAmount);
    }

    private static async Task<(Guid Tenant, Guid Employee)> SeedAsync(SqliteInMemoryDatabase database)
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); using var db = database.CreateContext(new TestTenantContext(tenantId)); db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"VQ{tenantId:N}"[..10], TenantName = "Variable pay", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active }); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"VQ-{employeeId:N}"[..12], FirstName = "Variable", LastName = "Pay", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active }); await db.SaveChangesAsync(); return (tenantId, employeeId);
    }
}
