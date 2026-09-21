using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollLoansProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
    {
        var tenantId = Guid.NewGuid();
        var maker = Guid.NewGuid();
        var checker = Guid.NewGuid();
        tenant.TenantId = tenantId;
        tenant.UserId = maker;

        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"LN{tenantId:N}"[..12], TenantName = "Loans provider test", Host = $"{tenantId:N}.loans.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql });
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"LN-{tenantId:N}"[..12], FirstName = "Loan", LastName = "Employee", Email = $"{tenantId:N}@loans.test", DateOfJoining = new DateOnly(2024, 1, 1), Status = EmployeeStatus.Active };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var service = new PayrollLoanService(db, tenant, TimeProvider.System);
        var product = await service.CreateProductAsync(new LoanProductRequest
        {
            Code = $"LP-{tenantId:N}"[..12], Name = "Provider loan", ProductType = LoanProductType.Loan,
            CurrencyCode = "INR", IsActive = true, MinAmount = 100, MaxAmount = 10000,
            MinTenureMonths = 1, MaxTenureMonths = 12, InterestMethod = LoanInterestMethod.None,
            AllowPartialPrepayment = true, AllowEarlyClosure = true, RecoveryPolicy = LoanRecoveryPolicy.PartialRecovery
        });
        Assert.True(product.Succeeded, product.Message);

        var updated = await service.UpdateProductAsync(product.Value!.Id, new LoanProductRequest
        {
            Code = product.Value.Code, Name = "Provider loan updated", ProductType = LoanProductType.Loan,
            CurrencyCode = "INR", IsActive = true, MinAmount = 100, MaxAmount = 10000,
            MinTenureMonths = 1, MaxTenureMonths = 12, InterestMethod = LoanInterestMethod.None,
            AllowPartialPrepayment = true, AllowEarlyClosure = true, RecoveryPolicy = LoanRecoveryPolicy.PartialRecovery
        });
        Assert.True(updated.Succeeded, updated.Message);

        var overlap = await service.CreateProductVersionAsync(product.Value.Id, new LoanProductVersionRequest
        {
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date), EffectiveTo = null,
            MinAmount = 100, MaxAmount = 10000, MinTenureMonths = 1, MaxTenureMonths = 12,
            InterestMethod = LoanInterestMethod.None, InterestRate = 0,
            AllowPartialPrepayment = true, AllowEarlyClosure = true, RecoveryPolicy = LoanRecoveryPolicy.PartialRecovery
        });
        Assert.Equal(ResultStatus.Conflict, overlap.Status);

        var loan = await service.CreateLoanAsync(new LoanRequest { EmployeeId = employee.Id, LoanProductId = product.Value.Id, RequestedAmount = 1200, RequestedTenureMonths = 3, CurrencyCode = "INR" });
        Assert.True(loan.Succeeded, loan.Message);
        var submitted = await service.SubmitAsync(loan.Value!.Id);
        Assert.True(submitted.Succeeded, submitted.Message);
        var persistedLoan = await db.EmployeeLoans.AsNoTracking().SingleAsync(x => x.Id == loan.Value.Id);
        Assert.Equal(2, persistedLoan.ConcurrencyVersion);

        tenant.UserId = checker;
        var approved = await service.ApproveAsync(loan.Value.Id);
        Assert.True(approved.Succeeded, approved.Message);
        var existingInstallments = await db.LoanInstallments.AsNoTracking().Where(x => x.EmployeeLoanId == loan.Value.Id).ToListAsync();
        Assert.Empty(existingInstallments);
        var active = await service.RecordDisbursementAsync(loan.Value.Id);
        Assert.True(active.Succeeded, active.Message);
        Assert.NotEmpty(active.Value!.Installments);

        var repayment = await service.RecordRepaymentAsync(loan.Value.Id, new LoanRepaymentRequest { Amount = 100, Reference = "provider-manual" });
        Assert.True(repayment.Succeeded, repayment.Message);
        var prepaid = await service.PartialPrepayAsync(loan.Value.Id, new LoanRepaymentRequest { Amount = 50, Reference = "provider-prepay" });
        Assert.True(prepaid.Succeeded, prepaid.Message);

        var settlementService = new PayrollRetroSettlementService(db, tenant, TimeProvider.System);
        var settlement = await settlementService.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employee.Id, SeparationDate = new DateOnly(2026, 12, 31), LastWorkingDate = new DateOnly(2026, 12, 31), SettlementDate = new DateOnly(2026, 12, 31), CurrencyCode = "INR" });
        Assert.True(settlement.Succeeded, settlement.Message);
        Assert.True((await settlementService.AddSettlementLineAsync(settlement.Value!.Id, new FinalSettlementLineRequest { LineType = FinalSettlementLineType.Other, ComponentCode = "FINAL-PAY", Description = "Final pay", Amount = 5000, IsEarning = true })).Succeeded);
        var calculated = await settlementService.CalculateSettlementAsync(settlement.Value.Id);
        Assert.True(calculated.Succeeded, calculated.Message);
        Assert.Contains(calculated.Value!.Lines, x => x.SourceType == "EmployeeLoan" && x.SourceId == loan.Value.Id);
        Assert.True((await settlementService.ApproveSettlementAsync(settlement.Value.Id)).Succeeded);
        var finalized = await settlementService.FinalizeSettlementAsync(settlement.Value.Id);
        Assert.True(finalized.Succeeded, finalized.Message);

        var register = await service.GetRegisterAsync(new LoanRegisterQuery { Page = 1, PageSize = 50 });
        Assert.True(register.Succeeded, register.Message);
        Assert.Contains(register.Value!.Items, x => x.Id == loan.Value.Id);

        tenant.TenantId = Guid.NewGuid();
        Assert.Empty(await db.EmployeeLoans.AsNoTracking().ToListAsync());
        await transaction.RollbackAsync();
    }
}
