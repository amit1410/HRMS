using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

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
    public async Task Final_settlement_creator_is_persisted_and_cannot_self_approve_when_required()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var makerId = Guid.NewGuid();
        var checkerId = Guid.NewGuid();
        await SeedAsync(database, tenantId, employeeId);

        await using (var setup = database.CreateContext(new TestTenantContext(tenantId, makerId)))
        {
            setup.PayrollControlConfigurations.Add(new PayrollControlConfiguration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RequireMakerChecker = true,
                PreventSelfApproval = true,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        Guid settlementId;
        await using (var makerDb = database.CreateContext(new TestTenantContext(tenantId, makerId)))
        {
            var makerService = new PayrollRetroSettlementService(makerDb, new TestTenantContext(tenantId, makerId), TimeProvider.System);
            var created = await makerService.CreateSettlementAsync(new FinalSettlementRequest
            {
                EmployeeId = employeeId,
                SeparationDate = new DateOnly(2026, 9, 30),
                LastWorkingDate = new DateOnly(2026, 9, 30),
                SettlementDate = new DateOnly(2026, 10, 5)
            });
            Assert.True(created.Succeeded, created.Message);
            settlementId = created.Value!.Id;
            await makerService.AddSettlementLineAsync(settlementId, new FinalSettlementLineRequest
            {
                LineType = FinalSettlementLineType.UnpaidSalary,
                ComponentCode = "SALARY",
                Description = "Unpaid salary",
                Amount = 100m,
                IsEarning = true
            });
            await makerService.CalculateSettlementAsync(settlementId);
            Assert.Equal(makerId, (await makerDb.FinalSettlementCases.SingleAsync(x => x.Id == settlementId)).CreatedByUserId);
            Assert.Equal(ResultStatus.ValidationFailed, (await makerService.ApproveSettlementAsync(settlementId)).Status);
        }

        await using var checkerDb = database.CreateContext(new TestTenantContext(tenantId, checkerId));
        var checkerService = new PayrollRetroSettlementService(checkerDb, new TestTenantContext(tenantId, checkerId), TimeProvider.System);
        Assert.True((await checkerService.ApproveSettlementAsync(settlementId)).Succeeded);
    }

    [Fact]
    public async Task Final_settlement_finalization_competing_transitions_have_one_winner()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedAsync(database, tenantId, employeeId);

        Guid settlementId;
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var service = new PayrollRetroSettlementService(setup, new TestTenantContext(tenantId), TimeProvider.System);
            var created = await service.CreateSettlementAsync(new FinalSettlementRequest
            {
                EmployeeId = employeeId,
                SeparationDate = new DateOnly(2026, 9, 30),
                LastWorkingDate = new DateOnly(2026, 9, 30),
                SettlementDate = new DateOnly(2026, 10, 5)
            });
            Assert.True(created.Succeeded, created.Message);
            settlementId = created.Value!.Id;
            Assert.True((await service.AddSettlementLineAsync(settlementId, new FinalSettlementLineRequest
            {
                LineType = FinalSettlementLineType.UnpaidSalary,
                ComponentCode = "SALARY",
                Description = "Unpaid salary",
                Amount = 100m,
                IsEarning = true
            })).Succeeded);
            Assert.True((await service.CalculateSettlementAsync(settlementId)).Succeeded);
            Assert.True((await service.ApproveSettlementAsync(settlementId)).Succeeded);
        }

        async Task<Result<FinalSettlementDto>> FinalizeAsync(Guid userId)
        {
            await using var context = database.CreateContext(new TestTenantContext(tenantId, userId));
            try
            {
                return await new PayrollRetroSettlementService(context, new TestTenantContext(tenantId, userId), TimeProvider.System).FinalizeSettlementAsync(settlementId);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<FinalSettlementDto>.Conflict("Concurrent finalization lost.");
            }
        }

        var outcomes = await Task.WhenAll(FinalizeAsync(Guid.NewGuid()), FinalizeAsync(Guid.NewGuid()));
        Assert.Equal(1, outcomes.Count(x => x.Succeeded));
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        Assert.Equal(FinalSettlementStatus.Finalized, (await verify.FinalSettlementCases.SingleAsync(x => x.Id == settlementId)).Status);
        Assert.Equal(1, await verify.FinalSettlementHistories.CountAsync(x => x.FinalSettlementCaseId == settlementId && x.ChangeType == FinalSettlementHistoryChangeType.Finalized));
    }

    [Fact]
    public async Task Retro_and_settlement_are_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var employeeA = Guid.NewGuid(); await SeedAsync(database, tenantA, employeeA); await SeedTenantOnlyAsync(database, tenantB); await using var db = database.CreateContext(new TestTenantContext(tenantA)); var service = new PayrollRetroSettlementService(db, new TestTenantContext(tenantA), TimeProvider.System); var created = await service.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeA, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 10, 5) }); Assert.True(created.Succeeded); await using var otherDb = database.CreateContext(new TestTenantContext(tenantB)); var otherService = new PayrollRetroSettlementService(otherDb, new TestTenantContext(tenantB), TimeProvider.System); Assert.False((await otherService.AddSettlementLineAsync(created.Value!.Id, new FinalSettlementLineRequest { LineType = FinalSettlementLineType.Other, ComponentCode = "X", Description = "Cross tenant", Amount = 1, IsEarning = true })).Succeeded);
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId) { await SeedTenantOnlyAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"E{employeeId:N}"[..10], FirstName = "Retro", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new DateOnly(2025, 1, 1) }); await db.SaveChangesAsync(); }
    private static async Task SeedTenantOnlyAsync(SqliteInMemoryDatabase database, Guid tenantId) { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RJ{tenantId:N}"[..12], TenantName = "Retro test tenant", Host = $"{tenantId:N}.retro.test", ShardKey = tenantId.ToString("N") }); await db.SaveChangesAsync(); }
}
