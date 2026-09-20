using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollRetroSettlementTests
{
    [Fact]
    public async Task Retro_case_evaluation_preserves_historical_results_and_supports_no_impact()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollRetroSettlementService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var created = await service.CreateRetroCaseAsync(new PayrollRetroCaseRequest { EmployeeId = employeeId, TriggerType = PayrollRetroTriggerType.ManualCorrection, EffectiveFrom = new DateOnly(2026, 1, 1) }); Assert.True(created.Succeeded, created.Message);
        var evaluated = await service.EvaluateRetroAsync(created.Value!.Id); Assert.True(evaluated.Succeeded, evaluated.Message); Assert.Equal(PayrollRetroStatus.NoImpact, evaluated.Value!.Status); Assert.Empty(evaluated.Value.Results);
    }

    [Fact]
    public async Task Final_settlement_calculates_lines_and_has_immutable_lifecycle()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollRetroSettlementService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var created = await service.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 10, 5) }); Assert.True(created.Succeeded, created.Message);
        Assert.True((await service.AddSettlementLineAsync(created.Value!.Id, new FinalSettlementLineRequest { LineType = FinalSettlementLineType.UnpaidSalary, ComponentCode = "SALARY", Description = "Unpaid salary", Amount = 10000, IsEarning = true })).Succeeded);
        Assert.True((await service.AddSettlementLineAsync(created.Value.Id, new FinalSettlementLineRequest { LineType = FinalSettlementLineType.NoticeRecovery, ComponentCode = "NOTICE", Description = "Notice recovery", Amount = 1500, IsDeduction = true })).Succeeded);
        var calculated = await service.CalculateSettlementAsync(created.Value.Id); Assert.True(calculated.Succeeded, calculated.Message); Assert.Equal(10000m, calculated.Value!.GrossPayable); Assert.Equal(1500m, calculated.Value.TotalDeductions); Assert.Equal(8500m, calculated.Value.NetSettlement);
        Assert.True((await service.ApproveSettlementAsync(created.Value.Id)).Succeeded); var finalized = await service.FinalizeSettlementAsync(created.Value.Id); Assert.True(finalized.Succeeded, finalized.Message); Assert.Equal(FinalSettlementStatus.Finalized, finalized.Value!.Status); Assert.False((await service.AddSettlementLineAsync(created.Value.Id, new FinalSettlementLineRequest { LineType = FinalSettlementLineType.Other, ComponentCode = "LATE", Description = "Late change", Amount = 1, IsEarning = true })).Succeeded);
    }

    [Fact]
    public async Task Settlement_duplicate_for_same_employee_and_separation_is_rejected()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollRetroSettlementService(db, new TestTenantContext(tenantId), TimeProvider.System); var request = new FinalSettlementRequest { EmployeeId = employeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 10, 5) }; Assert.True((await service.CreateSettlementAsync(request)).Succeeded); Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await service.CreateSettlementAsync(request)).Status);
    }

    [Fact]
    public async Task Retro_and_settlement_are_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var employeeA = Guid.NewGuid(); await SeedAsync(database, tenantA, employeeA); await SeedTenantOnlyAsync(database, tenantB); await using var db = database.CreateContext(new TestTenantContext(tenantA)); var service = new PayrollRetroSettlementService(db, new TestTenantContext(tenantA), TimeProvider.System); var created = await service.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeA, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 10, 5) }); Assert.True(created.Succeeded); await using var otherDb = database.CreateContext(new TestTenantContext(tenantB)); var otherService = new PayrollRetroSettlementService(otherDb, new TestTenantContext(tenantB), TimeProvider.System); Assert.False((await otherService.AddSettlementLineAsync(created.Value!.Id, new FinalSettlementLineRequest { LineType = FinalSettlementLineType.Other, ComponentCode = "X", Description = "Cross tenant", Amount = 1, IsEarning = true })).Succeeded);
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId) { await SeedTenantOnlyAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"E{employeeId:N}"[..10], FirstName = "Retro", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new DateOnly(2025, 1, 1) }); await db.SaveChangesAsync(); }
    private static async Task SeedTenantOnlyAsync(SqliteInMemoryDatabase database, Guid tenantId) { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RJ{tenantId:N}"[..12], TenantName = "Retro test tenant", Host = $"{tenantId:N}.retro.test", ShardKey = tenantId.ToString("N") }); await db.SaveChangesAsync(); }
}
