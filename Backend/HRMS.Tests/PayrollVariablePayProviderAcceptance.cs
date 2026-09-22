using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

internal static class PayrollVariablePayProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenantContext, DatabaseProviderType provider)
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            tenantContext.TenantId = tenantId; tenantContext.UserId = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"VPA{tenantId:N}"[..12], TenantName = $"Variable pay {provider}", Host = $"{tenantId:N}.variable.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active }); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"VPA-{employeeId:N}"[..12], FirstName = "Provider", LastName = "Variable Pay", Email = $"{employeeId:N}@provider.test", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active }); await db.SaveChangesAsync();
            var service = new VariablePayService(db, tenantContext, TimeProvider.System); var plan = await service.CreatePlanAsync(new VariablePayPlanRequest { Code = "PROVIDER", Name = "Provider bonus", PlanType = VariablePayPlanType.FixedBonus }); Assert.True(plan.Succeeded, plan.Message); var version = await service.AddVersionAsync(plan.Value!.Id, new VariablePayPlanVersionRequest { EffectiveFrom = new(2026, 1, 1), CalculationMethod = VariablePayCalculationMethod.FixedAmount, FixedAmount = 1000, TaxTreatment = VariablePayTaxTreatment.PartiallyTaxable, TaxablePercentage = 50, FinalSettlementTreatment = VariablePayFinalSettlementTreatment.IncludeOutstanding, AllowManualOverride = true }); Assert.True(version.Succeeded, version.Message);
            var request = new VariablePayAwardRequest { PlanVersionId = version.Value!.Versions.Single().Id, AwardPeriodFrom = new(2026, 1, 1), AwardPeriodTo = new(2026, 12, 31), EligibilityDate = new(2026, 12, 31) }; var preview = await service.PreviewAsync(employeeId, request); Assert.True(preview.Succeeded, preview.Message); Assert.Equal(1000m, preview.Value!.CalculatedAmount); var generated = await service.GenerateAsync(employeeId, request); Assert.True(generated.Succeeded, generated.Message); Assert.StartsWith("VPA/2026/", generated.Value!.AwardNumber); Assert.True((await service.SubmitAsync(generated.Value.Id)).Succeeded); tenantContext.UserId = Guid.NewGuid(); var approved = await service.ApproveAsync(generated.Value.Id); Assert.True(approved.Succeeded, approved.Message); Assert.Equal(1000m, approved.Value!.TaxableAmount + approved.Value.NonTaxableAmount); var finalSettlementId = Guid.NewGuid(); db.FinalSettlementCases.Add(new FinalSettlementCase { Id = finalSettlementId, TenantId = tenantId, EmployeeId = employeeId, SeparationDate = new(2026, 12, 31), LastWorkingDate = new(2026, 12, 31), SettlementDate = new(2026, 12, 31), EmployeeCodeSnapshot = "VPA-1", EmployeeNameSnapshot = "Provider Variable Pay" }); await db.SaveChangesAsync(); var settled = await service.SettleAsync(generated.Value.Id, new VariablePaySettlementRequest { SettlementType = VariablePaySettlementType.FinalSettlement, Amount = 1000m, FinalSettlementId = finalSettlementId }); Assert.True(settled.Succeeded, settled.Message); Assert.Equal(1, await db.VariablePaySettlements.CountAsync(x => x.TenantId == tenantId)); var other = new TestTenantContext(Guid.NewGuid()); Assert.False((await new VariablePayService(db, other, TimeProvider.System).GetAsync(generated.Value.Id)).Succeeded);
        }
        finally { await transaction.RollbackAsync(); }
    }
}
