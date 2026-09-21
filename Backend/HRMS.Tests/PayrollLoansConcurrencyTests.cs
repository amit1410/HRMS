using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollLoansConcurrencyTests
{
    [Fact]
    public async Task Payroll_recovery_vs_payroll_recovery_has_one_authoritative_financial_effect()
    {
        using var database = new SqliteInMemoryDatabase();
        var seed = await SeedActiveLoanAsync(database, 100m);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = database.CreateContext(new TestTenantContext(seed.TenantId));
            try { return (await new PayrollLoanService(db, new TestTenantContext(seed.TenantId), TimeProvider.System).RecordRepaymentAsync(seed.LoanId, new LoanRepaymentRequest { Amount = 100, Reference = "payroll-concurrency" })).Succeeded; }
            catch (DbUpdateConcurrencyException) { return false; }
        }));
        await using var verify = database.CreateContext(new TestTenantContext(seed.TenantId));
        Assert.Single(outcomes, x => x);
        Assert.Single(await verify.LoanRepayments.Where(x => x.EmployeeLoanId == seed.LoanId).ToListAsync());
        var loan = await verify.EmployeeLoans.SingleAsync(x => x.Id == seed.LoanId);
        Assert.Equal(0m, loan.OutstandingTotal);
        Assert.True(loan.OutstandingPrincipal >= 0 && loan.OutstandingInterest >= 0);
    }

    [Fact]
    public async Task Prepayment_vs_payroll_recovery_never_over_recovers_the_authoritative_balance()
    {
        using var database = new SqliteInMemoryDatabase();
        var seed = await SeedActiveLoanAsync(database, 100m, allowPrepayment: true);
        var outcomes = await Task.WhenAll(
            RunLoanOperationAsync(database, seed, service => service.PartialPrepayAsync(seed.LoanId, new LoanRepaymentRequest { Amount = 60, Reference = "prepayment-concurrency" })),
            RunLoanOperationAsync(database, seed, service => service.RecordRepaymentAsync(seed.LoanId, new LoanRepaymentRequest { Amount = 60, Reference = "payroll-concurrency" })));
        await using var verify = database.CreateContext(new TestTenantContext(seed.TenantId));
        var loan = await verify.EmployeeLoans.SingleAsync(x => x.Id == seed.LoanId);
        var recovered = await verify.LoanRepayments.Where(x => x.EmployeeLoanId == seed.LoanId).SumAsync(x => x.Amount);
        Assert.True(outcomes.Count(x => x) <= 1);
        Assert.InRange(recovered, 0m, 100m);
        Assert.True(loan.OutstandingPrincipal >= 0 && loan.OutstandingInterest >= 0 && loan.OutstandingTotal >= 0);
    }

    [Fact]
    public async Task Early_closure_vs_manual_repayment_has_one_valid_lifecycle_outcome()
    {
        using var database = new SqliteInMemoryDatabase();
        var seed = await SeedActiveLoanAsync(database, 100m, allowClosure: true);
        var outcomes = await Task.WhenAll(
            RunLoanOperationAsync(database, seed, service => service.CloseAsync(seed.LoanId, new LoanRepaymentRequest { Amount = 100, Reference = "closure-concurrency" })),
            RunLoanOperationAsync(database, seed, service => service.RecordRepaymentAsync(seed.LoanId, new LoanRepaymentRequest { Amount = 100, Reference = "manual-concurrency" })));
        await using var verify = database.CreateContext(new TestTenantContext(seed.TenantId));
        var loan = await verify.EmployeeLoans.SingleAsync(x => x.Id == seed.LoanId);
        Assert.True(outcomes.Count(x => x) <= 1);
        Assert.Contains(loan.Status, new[] { LoanStatus.Active, LoanStatus.Closed });
        Assert.True(loan.OutstandingTotal >= 0);
        Assert.True(await verify.LoanRepayments.CountAsync(x => x.EmployeeLoanId == seed.LoanId) <= 1);
    }

    [Fact]
    public async Task Final_settlement_recovery_vs_payroll_recovery_never_exceeds_outstanding()
    {
        using var database = new SqliteInMemoryDatabase();
        var seed = await SeedActiveLoanAsync(database, 100m);
        var settlementId = Guid.NewGuid();
        await using (var db = database.CreateContext(new TestTenantContext(seed.TenantId)))
        {
            db.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = seed.TenantId, EmployeeId = seed.EmployeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 10, 5), Status = FinalSettlementStatus.Finalized });
            await db.SaveChangesAsync();
        }
        var outcomes = await Task.WhenAll(
            RunLoanOperationAsync(database, seed, service => service.RecordRepaymentAsync(seed.LoanId, new LoanRepaymentRequest { Amount = 100, Reference = "payroll-vs-settlement" })),
            Task.Run(async () =>
            {
                await using var db = database.CreateContext(new TestTenantContext(seed.TenantId));
                try
                {
                    var changed = await db.EmployeeLoans.Where(x => x.TenantId == seed.TenantId && x.Id == seed.LoanId && x.ConcurrencyVersion == 1 && x.OutstandingTotal >= 100).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OutstandingPrincipal, 0m).SetProperty(x => x.OutstandingTotal, 0m).SetProperty(x => x.ConcurrencyVersion, 2));
                    if (changed != 1) return false;
                    db.LoanRepayments.Add(new LoanRepayment { Id = Guid.NewGuid(), TenantId = seed.TenantId, EmployeeLoanId = seed.LoanId, Amount = 100, PrincipalAmount = 100, RepaymentType = LoanRepaymentType.FinalSettlement, PaymentDate = new DateOnly(2026, 10, 5), SourceType = "FinalSettlement", Reference = settlementId.ToString(), CreatedByUserId = Guid.Empty });
                    await db.SaveChangesAsync(); return true;
                }
                catch (DbUpdateConcurrencyException) { return false; }
            }));
        await using var verify = database.CreateContext(new TestTenantContext(seed.TenantId));
        var loanAfter = await verify.EmployeeLoans.SingleAsync(x => x.Id == seed.LoanId);
        var recovered = await verify.LoanRepayments.Where(x => x.EmployeeLoanId == seed.LoanId).SumAsync(x => x.Amount);
        Assert.True(outcomes.Count(x => x) <= 1);
        Assert.InRange(recovered, 0m, 100m);
        Assert.True(loanAfter.OutstandingTotal >= 0);
    }
    [Fact]
    public async Task Approval_vs_approval_has_one_authoritative_state_and_history()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await AddTenantAsync(database, tenantId);
        var loanId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "CONC-001", FirstName = "Concurrency", LastName = "Employee", Email = "conc-001@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
            seed.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = tenantId, Code = "CONC", Name = "Concurrency loan", ProductType = LoanProductType.Loan, CurrencyCode = "INR" });
            seed.LoanProductVersions.Add(new LoanProductVersion { Id = versionId, TenantId = tenantId, LoanProductId = productId, EffectiveFrom = new DateOnly(2026, 1, 1), MinAmount = 1, MaxAmount = 10000, MinTenureMonths = 1, MaxTenureMonths = 12, Status = LoanProductVersionStatus.Active });
            seed.EmployeeLoans.Add(new EmployeeLoan
            {
                Id = loanId,
                TenantId = tenantId,
                EmployeeId = employeeId,
                LoanProductId = productId,
                LoanProductVersionId = versionId,
                LoanNumber = "LN/CONCURRENCY/001",
                RequestedAmount = 1000,
                RequestedTenureMonths = 1,
                Status = LoanStatus.Submitted,
                OutstandingPrincipal = 1000,
                OutstandingTotal = 1000,
                CurrencyCode = "INR"
            });
            await seed.SaveChangesAsync();
        }

        await using var first = database.CreateContext(new TestTenantContext(tenantId));
        await using var second = database.CreateContext(new TestTenantContext(tenantId));
        var firstLoan = await first.EmployeeLoans.SingleAsync(x => x.Id == loanId);
        var secondLoan = await second.EmployeeLoans.SingleAsync(x => x.Id == loanId);
        firstLoan.Status = LoanStatus.Approved;
        firstLoan.ApprovedAmount = 1000;
        firstLoan.ConcurrencyVersion++;
        first.LoanHistories.Add(new LoanHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeLoanId = loanId, EventType = LoanHistoryEventType.Approved, PreviousStatus = LoanStatus.Submitted, NewStatus = LoanStatus.Approved, OccurredAtUtc = DateTime.UtcNow });
        await first.SaveChangesAsync();

        secondLoan.Status = LoanStatus.Approved;
        secondLoan.ApprovedAmount = 1000;
        secondLoan.ConcurrencyVersion++;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        var authoritative = await database.CreateContext(new TestTenantContext(tenantId)).EmployeeLoans.AsNoTracking().SingleAsync(x => x.Id == loanId);
        Assert.Equal(LoanStatus.Approved, authoritative.Status);
        Assert.Equal(2, authoritative.ConcurrencyVersion);
        await using var history = database.CreateContext(new TestTenantContext(tenantId));
        Assert.Single(await history.LoanHistories.Where(x => x.EmployeeLoanId == loanId && x.EventType == LoanHistoryEventType.Approved).ToListAsync());
    }

    [Fact]
    public async Task Schedule_generation_vs_schedule_generation_has_one_unique_installment_set()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await AddTenantAsync(database, tenantId);
        var loanId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "CONC-002", FirstName = "Concurrency", LastName = "Employee", Email = "conc-002@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
            seed.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = tenantId, Code = "CONC2", Name = "Concurrency loan 2", ProductType = LoanProductType.Loan, CurrencyCode = "INR" });
            seed.LoanProductVersions.Add(new LoanProductVersion { Id = versionId, TenantId = tenantId, LoanProductId = productId, EffectiveFrom = new DateOnly(2026, 1, 1), MinAmount = 1, MaxAmount = 10000, MinTenureMonths = 1, MaxTenureMonths = 12, Status = LoanProductVersionStatus.Active });
            seed.EmployeeLoans.Add(new EmployeeLoan { Id = loanId, TenantId = tenantId, EmployeeId = employeeId, LoanProductId = productId, LoanProductVersionId = versionId, LoanNumber = "LN/CONCURRENCY/002", RequestedAmount = 1000, ApprovedAmount = 1000, RequestedTenureMonths = 1, ApprovedTenureMonths = 1, Status = LoanStatus.Active, OutstandingPrincipal = 1000, OutstandingTotal = 1000, CurrencyCode = "INR" });
            seed.LoanInstallments.Add(new LoanInstallment { Id = installmentId, TenantId = tenantId, EmployeeLoanId = loanId, InstallmentNumber = 1, DueDate = new DateOnly(2026, 10, 1), OpeningPrincipal = 1000, PrincipalAmount = 1000, InstallmentAmount = 1000, ClosingPrincipal = 0, Status = LoanInstallmentStatus.Scheduled });
            await seed.SaveChangesAsync();
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var context = database.CreateContext(new TestTenantContext(tenantId));
            context.LoanInstallments.Add(new LoanInstallment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeLoanId = loanId, InstallmentNumber = 2, DueDate = new DateOnly(2026, 11, 1), OpeningPrincipal = 0, PrincipalAmount = 0, InstallmentAmount = 0, ClosingPrincipal = 0, Status = LoanInstallmentStatus.Scheduled });
            try { await context.SaveChangesAsync(); return true; } catch (DbUpdateException) { return false; }
        }));
        await using var read = database.CreateContext(new TestTenantContext(tenantId));
        var installments = await read.LoanInstallments.AsNoTracking().Where(x => x.EmployeeLoanId == loanId).OrderBy(x => x.InstallmentNumber).ToListAsync();
        Assert.Single(outcomes, x => x);
        Assert.Equal(2, installments.Count);
        Assert.Equal(new[] { 1, 2 }, installments.Select(x => x.InstallmentNumber));
        Assert.Equal(1000m, installments.Sum(x => x.PrincipalAmount));
    }

    private static async Task AddTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var catalog = database.CreateContext(new TestTenantContext());
        catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"CC{tenantId:N}"[..12], TenantName = "Concurrency tenant", Host = $"{tenantId:N}.concurrency.test", ShardKey = tenantId.ToString("N") });
        await catalog.SaveChangesAsync();
    }

    private static async Task<bool> RunLoanOperationAsync(SqliteInMemoryDatabase database, ActiveLoanSeed seed, Func<PayrollLoanService, Task<HRMS.Application.Common.Result<EmployeeLoanDto>>> operation)
    {
        await using var db = database.CreateContext(new TestTenantContext(seed.TenantId));
        try { return (await operation(new PayrollLoanService(db, new TestTenantContext(seed.TenantId), TimeProvider.System))).Succeeded; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException) { return false; }
    }

    private static async Task<ActiveLoanSeed> SeedActiveLoanAsync(SqliteInMemoryDatabase database, decimal balance, bool allowPrepayment = false, bool allowClosure = false)
    {
        var tenantId = Guid.NewGuid(); await AddTenantAsync(database, tenantId); var employeeId = Guid.NewGuid(); var productId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var loanId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"CON-{loanId:N}"[..10], FirstName = "Concurrent", LastName = "Loan", Email = $"{loanId:N}@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
        db.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = tenantId, Code = $"C{loanId:N}"[..8], Name = "Concurrency loan", ProductType = LoanProductType.Loan, CurrencyCode = "INR", AllowPartialPrepayment = allowPrepayment, AllowEarlyClosure = allowClosure });
        db.LoanProductVersions.Add(new LoanProductVersion { Id = versionId, TenantId = tenantId, LoanProductId = productId, EffectiveFrom = new DateOnly(2026, 1, 1), MinAmount = 1, MaxAmount = 10000, MinTenureMonths = 1, MaxTenureMonths = 12, Status = LoanProductVersionStatus.Active, AllowPartialPrepayment = allowPrepayment, AllowEarlyClosure = allowClosure });
        db.EmployeeLoans.Add(new EmployeeLoan { Id = loanId, TenantId = tenantId, EmployeeId = employeeId, LoanProductId = productId, LoanProductVersionId = versionId, LoanNumber = $"LN/{loanId:N}"[..15], RequestedAmount = balance, ApprovedAmount = balance, RequestedTenureMonths = 1, ApprovedTenureMonths = 1, Status = LoanStatus.Active, OutstandingPrincipal = balance, OutstandingTotal = balance, CurrencyCode = "INR", RequestedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(); return new ActiveLoanSeed(tenantId, employeeId, loanId);
    }

    private sealed record ActiveLoanSeed(Guid TenantId, Guid EmployeeId, Guid LoanId);
}
