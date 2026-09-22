using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollAdjustmentsConcurrencyTests
{
    [Fact]
    public async Task Concurrent_submit_has_one_authoritative_transition_and_history_effect()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId); var maker = Guid.NewGuid();
        await using var setup = database.CreateContext(new TestTenantContext(tenantId, maker)); var context = new TestTenantContext(tenantId, maker); var service = new PayrollAdjustmentService(setup, context, TimeProvider.System, new PayrollRunService(setup, context, TimeProvider.System)); var created = await service.CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = new(2026, 9, 1), Description = "Concurrent submission", Amount = 100 }); Assert.True(created.Succeeded, created.Message);
        async Task<bool> SubmitAsync()
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, maker));
            try { return (await new PayrollAdjustmentService(db, new TestTenantContext(tenantId, maker), TimeProvider.System, new PayrollRunService(db, new TestTenantContext(tenantId, maker), TimeProvider.System)).SubmitAsync(created.Value!.Id)).Succeeded; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        var outcomes = await Task.WhenAll(SubmitAsync(), SubmitAsync());
        Assert.Equal(1, outcomes.Count(x => x)); await using var verify = database.CreateContext(new TestTenantContext(tenantId)); Assert.Equal(PayrollAdjustmentStatus.Submitted, (await verify.PayrollAdjustments.SingleAsync(x => x.Id == created.Value!.Id)).Status); Assert.Single(await verify.PayrollAdjustmentHistories.Where(x => x.PayrollAdjustmentId == created.Value.Id && x.EventType == PayrollAdjustmentHistoryEventType.Submitted).ToListAsync());
    }

    [Fact]
    public async Task Independent_number_allocations_are_unique_and_tenant_year_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId);
        var numbers = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid())); var context = new TestTenantContext(tenantId, Guid.NewGuid()); var result = await new PayrollAdjustmentService(db, context, TimeProvider.System, new PayrollRunService(db, context, TimeProvider.System)).CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = new(2026, 9, 1), Description = $"Number {i}", Amount = i + 1, SourceType = "NumberConcurrency", SourceReferenceId = Guid.NewGuid() }); Assert.True(result.Succeeded, result.Message); numbers.Add(result.Value!.AdjustmentNumber);
        }
        Assert.Equal(12, numbers.Distinct(StringComparer.Ordinal).Count()); Assert.Equal(Enumerable.Range(1, 12).Select(x => $"PADJ/2026/{x:D6}"), numbers);
    }

    [Fact]
    public async Task Concurrent_approve_has_one_authoritative_approval_effect()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId); var maker = Guid.NewGuid();
        await using var setup = database.CreateContext(new TestTenantContext(tenantId, maker)); var makerContext = new TestTenantContext(tenantId, maker); var makerService = new PayrollAdjustmentService(setup, makerContext, TimeProvider.System, new PayrollRunService(setup, makerContext, TimeProvider.System)); var created = await makerService.CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = new(2026, 9, 1), Description = "Concurrent approval", Amount = 100 }); Assert.True(created.Succeeded); Assert.True((await makerService.SubmitAsync(created.Value!.Id)).Succeeded);
        async Task<bool> ApproveAsync()
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid()));
            try { var context = new TestTenantContext(tenantId, Guid.NewGuid()); return (await new PayrollAdjustmentService(db, context, TimeProvider.System, new PayrollRunService(db, context, TimeProvider.System)).ApproveAsync(created.Value!.Id)).Succeeded; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        var outcomes = await Task.WhenAll(ApproveAsync(), ApproveAsync());
        Assert.Equal(1, outcomes.Count(x => x)); await using var verify = database.CreateContext(new TestTenantContext(tenantId)); Assert.Equal(PayrollAdjustmentStatus.Approved, (await verify.PayrollAdjustments.SingleAsync(x => x.Id == created.Value!.Id)).Status); Assert.Single(await verify.PayrollAdjustmentHistories.Where(x => x.PayrollAdjustmentId == created.Value.Id && x.EventType == PayrollAdjustmentHistoryEventType.Approved).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_off_cycle_scheduling_applies_an_adjustment_once()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var maker = Guid.NewGuid(); var checker = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId);
        var periodId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId, maker)))
        {
            setup.Users.AddRange(new User { Id = maker, TenantId = tenantId, Email = $"{maker:N}@test.local", FirstName = "Maker", LastName = "Adjustment" }, new User { Id = checker, TenantId = tenantId, Email = $"{checker:N}@test.local", FirstName = "Checker", LastName = "Adjustment" });
            setup.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "CONCURRENCY-2026-09", Name = "Concurrency period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open }); await setup.SaveChangesAsync();
            var context = new TestTenantContext(tenantId, maker); var service = new PayrollAdjustmentService(setup, context, TimeProvider.System, new PayrollRunService(setup, context, TimeProvider.System)); var created = await service.CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = new(2026, 9, 1), Description = "Concurrent off-cycle scheduling", Amount = 100 }); Assert.True(created.Succeeded, created.Message); Assert.True((await service.SubmitAsync(created.Value!.Id)).Succeeded); context.UserId = checker; Assert.True((await service.ApproveAsync(created.Value.Id)).Succeeded);
            async Task<bool> ScheduleAsync()
            {
                await using var db = database.CreateContext(new TestTenantContext(tenantId, checker)); var scoped = new TestTenantContext(tenantId, checker); try { return (await new PayrollAdjustmentService(db, scoped, TimeProvider.System, new PayrollRunService(db, scoped, TimeProvider.System)).CreateOffCycleRunAsync(new PayrollOffCycleRunRequest { PayrollPeriodId = periodId, EffectiveDate = new(2026, 10, 5), PaymentDate = new(2026, 10, 5), AdjustmentIds = [created.Value.Id] })).Succeeded; } catch (DbUpdateException) { return false; }
            }
            var outcomes = await Task.WhenAll(ScheduleAsync(), ScheduleAsync()); Assert.Equal(1, outcomes.Count(x => x));
        }
        await using var verify = database.CreateContext(new TestTenantContext(tenantId)); var adjustment = await verify.PayrollAdjustments.SingleAsync(x => x.EmployeeId == employeeId); Assert.Equal(PayrollAdjustmentStatus.Scheduled, adjustment.Status); Assert.NotNull(adjustment.TargetPayrollRunId); Assert.Equal(1, await verify.PayrollRuns.CountAsync(x => x.TenantId == tenantId && x.RunType == PayrollRunType.OffCycle));
    }

    [Fact]
    public async Task Concurrent_reversal_requests_create_one_authoritative_reversal()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var userId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId); var periodId = Guid.NewGuid(); var runId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId, userId))) { setup.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@test.local", FirstName = "Reversal", LastName = "Checker" }); setup.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "REVERSAL-2026-09", Name = "Reversal period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open }); setup.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "REVERSAL-RUN", RunType = PayrollRunType.Regular, Status = PayrollRunStatus.Finalized }); await setup.SaveChangesAsync(); }
        async Task<bool> RequestAsync()
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, userId)); var context = new TestTenantContext(tenantId, userId); try { return (await new PayrollAdjustmentService(db, context, TimeProvider.System, new PayrollRunService(db, context, TimeProvider.System)).RequestReversalAsync(new PayrollReversalRequest { OriginalPayrollRunId = runId, Reason = "Concurrent reversal request" })).Succeeded; } catch (DbUpdateException) { return false; }
        }
        var outcomes = await Task.WhenAll(RequestAsync(), RequestAsync()); Assert.Equal(1, outcomes.Count(x => x)); await using var verify = database.CreateContext(new TestTenantContext(tenantId)); Assert.Single(await verify.PayrollReversals.Where(x => x.TenantId == tenantId && x.OriginalPayrollRunId == runId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_payroll_applications_are_duplicate_protected()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId); var periodId = Guid.NewGuid(); var runId = Guid.NewGuid(); var adjustmentId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId))) { setup.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "APPLICATION-2026-09", Name = "Application period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open }); setup.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "APPLICATION-RUN", RunType = PayrollRunType.OffCycle, Status = PayrollRunStatus.Prepared }); setup.PayrollAdjustments.Add(new PayrollAdjustment { Id = adjustmentId, TenantId = tenantId, EmployeeId = employeeId, SourceType = "ApplicationConcurrency", SourceId = Guid.NewGuid(), AdjustmentType = PayrollAdjustmentType.AdditionalEarning, ComponentCode = "ADJ", Description = "Application concurrency", Amount = 100, AdjustmentNumber = "PADJ/2026/999999", EffectiveDate = new(2026, 9, 1), Status = PayrollAdjustmentStatus.Scheduled }); await setup.SaveChangesAsync(); }
        async Task<bool> ApplyAsync()
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.PayrollAdjustmentApplications.Add(new PayrollAdjustmentApplication { Id = Guid.NewGuid(), TenantId = tenantId, PayrollAdjustmentId = adjustmentId, PayrollRunId = runId, AppliedAmount = 100, AppliedDate = new(2026, 9, 30) }); try { await db.SaveChangesAsync(); return true; } catch (DbUpdateException) { return false; }
        }
        var outcomes = await Task.WhenAll(ApplyAsync(), ApplyAsync()); Assert.Equal(1, outcomes.Count(x => x)); await using var verify = database.CreateContext(new TestTenantContext(tenantId)); Assert.Single(await verify.PayrollAdjustmentApplications.Where(x => x.TenantId == tenantId && x.PayrollAdjustmentId == adjustmentId && x.PayrollRunId == runId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_settlement_updates_allow_one_authoritative_applied_amount()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var adjustmentId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId);
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId))) { setup.PayrollAdjustments.Add(new PayrollAdjustment { Id = adjustmentId, TenantId = tenantId, EmployeeId = employeeId, SourceType = "SettlementRace", SourceId = Guid.NewGuid(), AdjustmentType = PayrollAdjustmentType.AdditionalEarning, ComponentCode = "ADJ", Description = "Settlement race", Amount = 100, AdjustmentNumber = "PADJ/2026/999998", EffectiveDate = new(2026, 9, 1), Status = PayrollAdjustmentStatus.Approved }); await setup.SaveChangesAsync(); }
        await using var dbA = database.CreateContext(new TestTenantContext(tenantId)); await using var dbB = database.CreateContext(new TestTenantContext(tenantId)); var adjustmentA = await dbA.PayrollAdjustments.SingleAsync(x => x.Id == adjustmentId); var adjustmentB = await dbB.PayrollAdjustments.SingleAsync(x => x.Id == adjustmentId); adjustmentA.AppliedAmount = 100; adjustmentA.Status = PayrollAdjustmentStatus.Applied; adjustmentA.ConcurrencyVersion++; adjustmentB.AppliedAmount = 100; adjustmentB.Status = PayrollAdjustmentStatus.Applied; adjustmentB.ConcurrencyVersion++;
        async Task<bool> SaveAsync(HrmsDbContext db) { try { await db.SaveChangesAsync(); return true; } catch (DbUpdateConcurrencyException) { return false; } }
        var outcomes = await Task.WhenAll(SaveAsync(dbA), SaveAsync(dbB)); Assert.Equal(1, outcomes.Count(x => x)); await using var verify = database.CreateContext(new TestTenantContext(tenantId)); var settled = await verify.PayrollAdjustments.SingleAsync(x => x.Id == adjustmentId); Assert.Equal(100, settled.AppliedAmount); Assert.Equal(0, settled.Amount - settled.AppliedAmount);
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId)
    {
        await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"PC{tenantId:N}"[..10], TenantName = "Payroll adjustment concurrency", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active }); await db.SaveChangesAsync();
        await using var scoped = database.CreateContext(new TestTenantContext(tenantId)); scoped.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"PC{employeeId:N}"[..10], FirstName = "Concurrency", LastName = "Adjustment", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active }); await scoped.SaveChangesAsync();
    }
}
