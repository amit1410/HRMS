using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollRetroSettlementProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"R7J{tenantId:N}"[..12], TenantName = "Retro provider tenant", Host = $"{tenantId:N}.retro.provider", ShardKey = tenantId.ToString("N") }); await db.SaveChangesAsync(); tenant.TenantId = tenantId;
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"E{employeeId:N}"[..10], FirstName = "Retro", LastName = "Provider", Email = $"{employeeId:N}@provider.test", DateOfJoining = new DateOnly(2025, 1, 1) }); await db.SaveChangesAsync();
        var service = new PayrollRetroSettlementService(db, tenant, TimeProvider.System); var created = await service.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 10, 5) }); Assert.True(created.Succeeded, created.Message); var line = await service.AddSettlementLineAsync(created.Value!.Id, new FinalSettlementLineRequest { LineType = FinalSettlementLineType.UnpaidSalary, ComponentCode = "SALARY", Description = "Provider salary", Amount = 1000m, IsEarning = true }); Assert.True(line.Succeeded, line.Message); var calculated = await service.CalculateSettlementAsync(created.Value.Id); Assert.True(calculated.Succeeded, calculated.Message); Assert.Equal(1000m, calculated.Value!.NetSettlement); Assert.True((await service.ApproveSettlementAsync(created.Value.Id)).Succeeded); var finalized = await service.FinalizeSettlementAsync(created.Value.Id); Assert.True(finalized.Succeeded, finalized.Message);
        var retro = await service.CreateRetroCaseAsync(new PayrollRetroCaseRequest { EmployeeId = employeeId, TriggerType = PayrollRetroTriggerType.ManualCorrection, EffectiveFrom = new DateOnly(2026, 1, 1) }); Assert.True(retro.Succeeded, retro.Message); var evaluated = await service.EvaluateRetroAsync(retro.Value!.Id); Assert.True(evaluated.Succeeded, evaluated.Message); Assert.Equal(PayrollRetroStatus.NoImpact, evaluated.Value!.Status);
    }
}
