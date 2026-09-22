using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class VariablePayLargeDataTests
{
    [Fact]
    public async Task One_hundred_awards_reconcile_and_remain_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var otherTenantId = Guid.NewGuid(); using var seed = database.CreateContext(new TestTenantContext(tenantId)); seed.Tenants.AddRange(new Tenant { Id = tenantId, TenantCode = "VQL", TenantName = "Large", Host = "vql.test", ShardKey = tenantId.ToString("N") }, new Tenant { Id = otherTenantId, TenantCode = "VQO", TenantName = "Other", Host = "vqo.test", ShardKey = otherTenantId.ToString("N") }); var plan = new VariablePayPlan { Id = Guid.NewGuid(), TenantId = tenantId, Code = "LARGE", Name = "Large", PlanType = VariablePayPlanType.FixedBonus }; var version = new VariablePayPlanVersion { Id = Guid.NewGuid(), TenantId = tenantId, VariablePayPlanId = plan.Id, EffectiveFrom = new(2026, 1, 1), Status = VariablePayPlanVersionStatus.Published, CalculationMethod = VariablePayCalculationMethod.FixedAmount, FixedAmount = 100 }; seed.VariablePayPlans.Add(plan); seed.VariablePayPlanVersions.Add(version); var employees = Enumerable.Range(1, 100).Select(i => new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"VQL-{i:000}", FirstName = "Large", LastName = i.ToString(), Email = $"vql{i}@test.local", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active }).ToList(); seed.Employees.AddRange(employees); await seed.SaveChangesAsync(); var tenant = new TestTenantContext(tenantId); using var db = database.CreateContext(tenant); var service = new VariablePayService(db, tenant, TimeProvider.System); foreach (var employee in employees) { var result = await service.GenerateAsync(employee.Id, new VariablePayAwardRequest { PlanVersionId = version.Id, AwardPeriodFrom = new(2026, 4, 1), AwardPeriodTo = new(2026, 4, 30), EligibilityDate = new(2026, 4, 1) }); Assert.True(result.Succeeded, result.Message); } var rows = await service.GetAwardsAsync(new VariablePayAwardQuery { Page = 1, PageSize = 50 }); Assert.True(rows.Succeeded); Assert.Equal(50, rows.Value!.Items.Count); Assert.Equal(100, rows.Value.TotalCount); Assert.Equal(10000m, await db.VariablePayAwards.Where(x => x.TenantId == tenantId).SumAsync(x => x.CalculatedAmount)); tenant.TenantId = otherTenantId; Assert.Empty((await service.GetAwardsAsync(new VariablePayAwardQuery()).ConfigureAwait(false)).Value!.Items);
    }
}
