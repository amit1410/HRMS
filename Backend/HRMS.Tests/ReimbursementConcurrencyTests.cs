using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using HRMS.Infrastructure.Persistence;

namespace HRMS.Tests;

public sealed class ReimbursementConcurrencyTests
{
    [Fact]
    public async Task Submit_and_approve_are_single_use_transitions()
    {
        var setup = await CreateApprovedClaimAsync(); using var database = setup.Database; var tenantId = setup.TenantId; var claimId = setup.ClaimId;
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System);
        Assert.False((await service.SubmitAsync(claimId)).Succeeded);
        Assert.False((await service.ApproveAsync(claimId, new ReimbursementApprovalRequest())).Succeeded);
        Assert.Single(await db.ReimbursementHistories.Where(x => x.ReimbursementClaimId == claimId && x.EventType == ReimbursementHistoryEventType.Approved).ToListAsync());
    }

    [Fact]
    public async Task Payroll_settlement_retry_is_idempotent_and_cannot_over_settle()
    {
        var setup = await CreateApprovedClaimAsync(); using var database = setup.Database; var tenantId = setup.TenantId; var claimId = setup.ClaimId; var lineId = setup.LineId;
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System); var payrollRun = Guid.NewGuid(); var payrollResult = Guid.NewGuid();
        var first = await service.RecordPayrollSettlementAsync(claimId, lineId, payrollRun, payrollResult, 100, new DateOnly(2026, 9, 30)); Assert.True(first.Succeeded, first.Message);
        var retry = await service.RecordPayrollSettlementAsync(claimId, lineId, payrollRun, payrollResult, 100, new DateOnly(2026, 9, 30)); Assert.False(retry.Succeeded);
        var row = await db.ReimbursementClaims.Include(x => x.Settlements).SingleAsync(x => x.Id == claimId); Assert.Equal(100m, row.SettledAmount); Assert.Single(row.Settlements); Assert.Equal(ReimbursementClaimStatus.Settled, row.Status);
    }

    [Fact]
    public async Task Manual_settlement_rejects_over_settlement_and_records_immutable_effect()
    {
        var setup = await CreateApprovedClaimAsync(); using var database = setup.Database; var tenantId = setup.TenantId; var claimId = setup.ClaimId;
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System);
        Assert.False((await service.SettleManuallyAsync(claimId, new ReimbursementSettlementRequest { Amount = 101, Reference = "too-much" })).Succeeded);
        var settled = await service.SettleManuallyAsync(claimId, new ReimbursementSettlementRequest { Amount = 100, Reference = "manual-100" }); Assert.True(settled.Succeeded, settled.Message);
        Assert.Single((await service.GetHistoryAsync(claimId)).Value!, x => x.EventType == ReimbursementHistoryEventType.ManualSettlement);
    }

    [Fact]
    public async Task Partial_manual_settlement_then_payroll_remainder_closes_claim_and_line()
    {
        var setup = await CreateApprovedClaimAsync(); using var database = setup.Database;
        await using (var manualDb = database.CreateContext(new TestTenantContext(setup.TenantId)))
        {
            var partial = await new ReimbursementService(manualDb, new TestTenantContext(setup.TenantId), TimeProvider.System).SettleManuallyAsync(setup.ClaimId, new ReimbursementSettlementRequest { Amount = 40, Reference = "manual-partial" }); Assert.True(partial.Succeeded, partial.Message); Assert.Equal(40m, partial.Value!.SettledAmount); Assert.Equal(ReimbursementClaimStatus.ReadyForSettlement, partial.Value.Status); Assert.Equal(ReimbursementClaimLineStatus.Approved, partial.Value.Lines[0].Status);
        }
        await using var payrollDb = database.CreateContext(new TestTenantContext(setup.TenantId)); var remainder = await new ReimbursementService(payrollDb, new TestTenantContext(setup.TenantId), TimeProvider.System).RecordPayrollSettlementAsync(setup.ClaimId, setup.LineId, Guid.NewGuid(), Guid.NewGuid(), 60, new DateOnly(2026, 9, 30)); Assert.True(remainder.Succeeded, remainder.Message); Assert.Equal(60m, remainder.Value!.Amount);
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); var claim = await verify.ReimbursementClaims.Include(x => x.Lines).SingleAsync(x => x.Id == setup.ClaimId); Assert.Equal(100m, claim.SettledAmount); Assert.Equal(0m, claim.TotalApprovedAmount - claim.SettledAmount); Assert.Equal(ReimbursementClaimStatus.Settled, claim.Status); Assert.Equal(ReimbursementClaimLineStatus.Settled, claim.Lines.Single().Status);
    }

    [Fact]
    public async Task Final_settlement_includes_approved_claim_once_and_preserves_traceability()
    {
        var setup = await CreateApprovedClaimAsync(ReimbursementSettlementMethod.FinalSettlement); using var database = setup.Database;
        Guid settlementId;
        await using (var db = database.CreateContext(new TestTenantContext(setup.TenantId)))
        {
            var service = new PayrollRetroSettlementService(db, new TestTenantContext(setup.TenantId), TimeProvider.System); var created = await service.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = (await db.ReimbursementClaims.SingleAsync(x => x.Id == setup.ClaimId)).EmployeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 9, 30), CurrencyCode = "INR" }); Assert.True(created.Succeeded, created.Message); settlementId = created.Value!.Id; var calculated = await service.CalculateSettlementAsync(settlementId); Assert.True(calculated.Succeeded, calculated.Message); Assert.Contains(calculated.Value!.Lines, x => x.SourceType == "ReimbursementClaimLine" && x.SourceId == setup.LineId); Assert.True((await service.ApproveSettlementAsync(settlementId)).Succeeded); Assert.True((await service.FinalizeSettlementAsync(settlementId)).Succeeded);
        }
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); var settlement = await verify.ReimbursementSettlements.SingleAsync(x => x.ReimbursementClaimLineId == setup.LineId && x.FinalSettlementId == settlementId); Assert.Equal(100m, settlement.Amount); Assert.Equal(ReimbursementSettlementType.FinalSettlement, settlement.SettlementType); Assert.False((await new PayrollRetroSettlementService(verify, new TestTenantContext(setup.TenantId), TimeProvider.System).FinalizeSettlementAsync(settlementId)).Succeeded);
    }

    [Fact]
    public async Task Concurrent_submit_attempts_create_one_submitted_transition()
    {
        var setup = await CreateClaimAsync(); using var database = setup.Database;
        var results = await Task.WhenAll(SubmitOnIndependentContext(database, setup.TenantId, setup.ClaimId), SubmitOnIndependentContext(database, setup.TenantId, setup.ClaimId));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); Assert.Equal(ReimbursementClaimStatus.Submitted, (await verify.ReimbursementClaims.SingleAsync(x => x.Id == setup.ClaimId)).Status); Assert.Single(await verify.ReimbursementHistories.Where(x => x.ReimbursementClaimId == setup.ClaimId && x.EventType == ReimbursementHistoryEventType.Submitted).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_approvals_have_one_authoritative_financial_effect()
    {
        var setup = await CreateSubmittedClaimAsync(); using var database = setup.Database;
        var results = await Task.WhenAll(ApproveOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, 100), ApproveOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, 100));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); var claim = await verify.ReimbursementClaims.Include(x => x.Lines).SingleAsync(x => x.Id == setup.ClaimId); Assert.Equal(100m, claim.TotalApprovedAmount); Assert.InRange(claim.TotalApprovedAmount, 0, claim.TotalClaimedAmount); Assert.Single(await verify.ReimbursementHistories.Where(x => x.ReimbursementClaimId == setup.ClaimId && x.EventType == ReimbursementHistoryEventType.Approved).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_partial_and_full_approval_never_exceeds_eligible_amount()
    {
        var setup = await CreateSubmittedClaimAsync(); using var database = setup.Database;
        var results = await Task.WhenAll(ApproveOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, 40), ApproveOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, 100));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); var claim = await verify.ReimbursementClaims.SingleAsync(x => x.Id == setup.ClaimId); Assert.InRange(claim.TotalApprovedAmount, 0, claim.TotalEligibleAmount); Assert.InRange(claim.TotalApprovedAmount, 0, claim.TotalClaimedAmount);
    }

    [Fact]
    public async Task Concurrent_payroll_settlements_create_one_effect_and_no_over_settlement()
    {
        var setup = await CreateApprovedClaimAsync(); using var database = setup.Database;
        var results = await Task.WhenAll(PayrollOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, Guid.NewGuid(), 100), PayrollOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, Guid.NewGuid(), 100));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); var claim = await verify.ReimbursementClaims.Include(x => x.Settlements).SingleAsync(x => x.Id == setup.ClaimId); Assert.Equal(100m, claim.SettledAmount); Assert.Single(claim.Settlements); Assert.Equal(ReimbursementClaimStatus.Settled, claim.Status);
    }

    [Fact]
    public async Task Concurrent_manual_and_payroll_settlement_cannot_exceed_outstanding()
    {
        var setup = await CreateApprovedClaimAsync(); using var database = setup.Database;
        var results = await Task.WhenAll(ManualOnIndependentContext(database, setup.TenantId, setup.ClaimId), PayrollOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, Guid.NewGuid(), 100));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); var claim = await verify.ReimbursementClaims.SingleAsync(x => x.Id == setup.ClaimId); Assert.InRange(claim.SettledAmount, 0, claim.TotalApprovedAmount); Assert.True(claim.TotalApprovedAmount - claim.SettledAmount >= 0);
    }

    [Fact]
    public async Task Payroll_cannot_settle_a_final_settlement_claim()
    {
        var setup = await CreateApprovedClaimAsync(ReimbursementSettlementMethod.FinalSettlement); using var database = setup.Database;
        var payroll = await PayrollOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, Guid.NewGuid(), 100);
        Assert.False(payroll);
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); Assert.Equal(0m, await verify.ReimbursementSettlements.Where(x => x.ReimbursementClaimId == setup.ClaimId).SumAsync(x => x.Amount));
    }

    [Fact]
    public async Task Concurrent_final_settlement_and_payroll_have_one_authoritative_effect()
    {
        var setup = await CreateApprovedClaimAsync(ReimbursementSettlementMethod.FinalSettlement); using var database = setup.Database; Guid finalSettlementId;
        await using (var db = database.CreateContext(new TestTenantContext(setup.TenantId)))
        {
            var employeeId = await db.ReimbursementClaims.Where(x => x.Id == setup.ClaimId).Select(x => x.EmployeeId).SingleAsync(); var service = new PayrollRetroSettlementService(db, new TestTenantContext(setup.TenantId), TimeProvider.System); var created = await service.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 9, 30), CurrencyCode = "INR" }); Assert.True(created.Succeeded, created.Message); finalSettlementId = created.Value!.Id; Assert.True((await service.CalculateSettlementAsync(finalSettlementId)).Succeeded); Assert.True((await service.ApproveSettlementAsync(finalSettlementId)).Succeeded);
        }
        var results = await Task.WhenAll(FinalizeOnIndependentContext(database, setup.TenantId, finalSettlementId), PayrollOnIndependentContext(database, setup.TenantId, setup.ClaimId, setup.LineId, Guid.NewGuid(), 100)); Assert.Equal(1, results.Count(x => x));
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); Assert.Equal(100m, await verify.ReimbursementSettlements.Where(x => x.ReimbursementClaimLineId == setup.LineId).SumAsync(x => x.Amount));
    }

    [Fact]
    public async Task Concurrent_monthly_limit_approvals_allow_only_one_claim()
    {
        var setup = await CreateTwoSubmittedClaimsWithMonthlyLimitAsync(); using var database = setup.Database;
        var results = await Task.WhenAll(ApproveOnIndependentContext(database, setup.TenantId, setup.FirstClaimId, setup.FirstLineId, 100), ApproveOnIndependentContext(database, setup.TenantId, setup.SecondClaimId, setup.SecondLineId, 100));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = database.CreateContext(new TestTenantContext(setup.TenantId)); Assert.Equal(100m, await verify.ReimbursementClaimLines.Where(x => x.TenantId == setup.TenantId).SumAsync(x => x.ApprovedAmount));
    }

    private static async Task<bool> SubmitOnIndependentContext(ConcurrentSqliteDatabase database, Guid tenantId, Guid claimId)
    { return await RetryTransientAsync(async () => { await using var db = database.CreateContext(new TestTenantContext(tenantId)); return (await new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System).SubmitAsync(claimId)).Succeeded; }); }
    private static async Task<bool> ApproveOnIndependentContext(ConcurrentSqliteDatabase database, Guid tenantId, Guid claimId, Guid lineId, decimal amount)
    { return await RetryTransientAsync(async () => { await using var db = database.CreateContext(new TestTenantContext(tenantId)); return (await new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System).ApproveAsync(claimId, new ReimbursementApprovalRequest { Lines = [new() { ClaimLineId = lineId, ApprovedAmount = amount }] })).Succeeded; }); }
    private static async Task<bool> PayrollOnIndependentContext(ConcurrentSqliteDatabase database, Guid tenantId, Guid claimId, Guid lineId, Guid resultId, decimal amount)
    { return await RetryTransientAsync(async () => { await using var db = database.CreateContext(new TestTenantContext(tenantId)); return (await new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System).RecordPayrollSettlementAsync(claimId, lineId, Guid.NewGuid(), resultId, amount, new DateOnly(2026, 9, 30))).Succeeded; }); }
    private static async Task<bool> ManualOnIndependentContext(ConcurrentSqliteDatabase database, Guid tenantId, Guid claimId)
    { return await RetryTransientAsync(async () => { await using var db = database.CreateContext(new TestTenantContext(tenantId)); return (await new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System).SettleManuallyAsync(claimId, new ReimbursementSettlementRequest { Amount = 100, Reference = Guid.NewGuid().ToString("N") })).Succeeded; }); }
    private static async Task<bool> FinalizeOnIndependentContext(ConcurrentSqliteDatabase database, Guid tenantId, Guid settlementId)
    { return await RetryTransientAsync(async () => { await using var db = database.CreateContext(new TestTenantContext(tenantId)); return (await new PayrollRetroSettlementService(db, new TestTenantContext(tenantId), TimeProvider.System).FinalizeSettlementAsync(settlementId)).Succeeded; }); }
    private static async Task<bool> RetryTransientAsync(Func<Task<bool>> operation)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await operation(); }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { await Task.Yield(); }
            catch (SqliteException) when (attempt < 2) { await Task.Yield(); }
        }
        return false;
    }

    private sealed record ApprovedClaimSetup(ConcurrentSqliteDatabase Database, Guid TenantId, Guid ClaimId, Guid LineId);
    private sealed record TwoClaimSetup(ConcurrentSqliteDatabase Database, Guid TenantId, Guid FirstClaimId, Guid FirstLineId, Guid SecondClaimId, Guid SecondLineId);

    private static Task<ApprovedClaimSetup> CreateClaimAsync() => CreateSubmittedClaimAsync(submit: false);

    private static async Task<ApprovedClaimSetup> CreateApprovedClaimAsync(ReimbursementSettlementMethod settlementMethod = ReimbursementSettlementMethod.Payroll)
    {
        var setup = await CreateSubmittedClaimAsync(settlementMethod); using var db = setup.Database.CreateContext(new TestTenantContext(setup.TenantId));
        var approved = await new ReimbursementService(db, new TestTenantContext(setup.TenantId), TimeProvider.System).ApproveAsync(setup.ClaimId, new ReimbursementApprovalRequest { Lines = [new() { ClaimLineId = setup.LineId, ApprovedAmount = 100 }] }); Assert.True(approved.Succeeded, approved.Message); return setup;
    }

    private static async Task<ApprovedClaimSetup> CreateSubmittedClaimAsync(ReimbursementSettlementMethod settlementMethod = ReimbursementSettlementMethod.Payroll, bool submit = true)
    {
        var database = new ConcurrentSqliteDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await using (var catalog = database.CreateContext(new TestTenantContext())) { catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RC{tenantId:N}"[..12], TenantName = "Concurrency tenant", Host = $"{tenantId:N}.claims.test", ShardKey = tenantId.ToString("N") }); await catalog.SaveChangesAsync(); }
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "CON-001", FirstName = "Concurrency", LastName = "Employee", Email = $"{tenantId:N}@claims.test", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active }); await db.SaveChangesAsync();
        var tenant = new TestTenantContext(tenantId); var service = new ReimbursementService(db, tenant, TimeProvider.System); var category = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "CON", Name = "Concurrency", CategoryType = ReimbursementCategoryType.Other }); Assert.True(category.Succeeded, category.Message); Assert.True((await service.CreatePolicyVersionAsync(category.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1) })).Succeeded);
        var claim = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = new DateOnly(2026, 9, 21), SettlementMethod = settlementMethod, Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 20), Description = "Concurrency claim", ClaimedAmount = 100 }] }); Assert.True(claim.Succeeded, claim.Message); var claimId = claim.Value!.Id; var lineId = claim.Value.Lines[0].Id; if (submit) Assert.True((await service.SubmitAsync(claimId)).Succeeded); return new ApprovedClaimSetup(database, tenantId, claimId, lineId);
    }

    private static async Task<TwoClaimSetup> CreateTwoSubmittedClaimsWithMonthlyLimitAsync()
    {
        var database = new ConcurrentSqliteDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await using (var catalog = database.CreateContext(new TestTenantContext())) { catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RL{tenantId:N}"[..12], TenantName = "Limit tenant", Host = $"{tenantId:N}.limit.test", ShardKey = tenantId.ToString("N") }); await catalog.SaveChangesAsync(); }
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "LIMIT-001", FirstName = "Limit", LastName = "Employee", Email = $"{tenantId:N}@limit.test", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active }); await db.SaveChangesAsync();
        var service = new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System); var category = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "LIMIT", Name = "Limited", CategoryType = ReimbursementCategoryType.Other }); Assert.True(category.Succeeded, category.Message); Assert.True((await service.CreatePolicyVersionAsync(category.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyLimit = 100 })).Succeeded);
        var first = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = new DateOnly(2026, 9, 1), Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 1), Description = "First limited", ClaimedAmount = 100 }] }); Assert.True(first.Succeeded, first.Message); Assert.True((await service.SubmitAsync(first.Value!.Id)).Succeeded);
        var second = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = new DateOnly(2026, 9, 2), Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 2), Description = "Second limited", ClaimedAmount = 100 }] }); Assert.True(second.Succeeded, second.Message); Assert.True((await service.SubmitAsync(second.Value!.Id)).Succeeded);
        return new TwoClaimSetup(database, tenantId, first.Value.Id, first.Value.Lines[0].Id, second.Value.Id, second.Value.Lines[0].Id);
    }

    private sealed class ConcurrentSqliteDatabase : IDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly string connectionString = $"Data Source=file:reimbursement-concurrency-{Guid.NewGuid():N}?mode=memory&cache=shared";

        public ConcurrentSqliteDatabase()
        {
            anchor = new SqliteConnection(connectionString); anchor.Open();
            using var context = CreateContext(new TestTenantContext()); context.Database.EnsureCreated();
        }

        public HrmsDbContext CreateContext(ITenantContext tenant)
        {
            var options = new DbContextOptionsBuilder<HrmsDbContext>().UseSqlite(connectionString).Options;
            return new HrmsDbContext(options, tenant);
        }

        public void Dispose() => anchor.Dispose();
    }
}
