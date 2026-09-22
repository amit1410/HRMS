using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class VariablePayConcurrencyTests
{
    [Fact]
    public async Task Repeated_generation_and_settlement_have_one_authoritative_effect()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); using (var seed = database.CreateContext(new TestTenantContext(tenantId))) { seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "VQC", TenantName = "Concurrency", Host = "vqc.test", ShardKey = tenantId.ToString("N") }); seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "VQC-1", FirstName = "Concurrency", LastName = "Employee", Email = "vqc@test.local", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active }); await seed.SaveChangesAsync(); }
        var tenant = new TestTenantContext(tenantId); using var db = database.CreateContext(tenant); var service = new VariablePayService(db, tenant, TimeProvider.System); var plan = await service.CreatePlanAsync(new VariablePayPlanRequest { Code = "RACE", Name = "Race", PlanType = VariablePayPlanType.FixedBonus }); var version = await service.AddVersionAsync(plan.Value!.Id, new VariablePayPlanVersionRequest { EffectiveFrom = new(2026, 1, 1), CalculationMethod = VariablePayCalculationMethod.FixedAmount, FixedAmount = 500 }); var request = new VariablePayAwardRequest { PlanVersionId = version.Value!.Versions.Single().Id, AwardPeriodFrom = new(2026, 3, 1), AwardPeriodTo = new(2026, 3, 31), EligibilityDate = new(2026, 3, 1) }; var a = await service.GenerateAsync(employeeId, request); var b = await service.GenerateAsync(employeeId, request); Assert.Equal(a.Value!.Id, b.Value!.Id); await service.SubmitAsync(a.Value.Id); tenant.UserId = Guid.NewGuid(); await service.ApproveAsync(a.Value.Id); var p1 = await service.SettleAsync(a.Value.Id, new VariablePaySettlementRequest { SettlementType = VariablePaySettlementType.Payroll, Amount = 500 }); Assert.True(p1.Succeeded); var p2 = await service.SettleAsync(a.Value.Id, new VariablePaySettlementRequest { SettlementType = VariablePaySettlementType.Payroll, Amount = 500 }); Assert.False(p2.Succeeded); Assert.Equal(1, await db.VariablePaySettlements.CountAsync());
    }
}
