using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollReimbursementsProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        var tenantId = Guid.NewGuid(); tenant.TenantId = tenantId; tenant.UserId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RC{tenantId:N}"[..12], TenantName = "Reimbursement provider test", Host = $"{tenantId:N}.claims.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active, DatabaseProvider = provider });
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"RC-{tenantId:N}"[..12], FirstName = "Claims", LastName = "Employee", Email = $"{tenantId:N}@claims.test", DateOfJoining = new DateOnly(2024, 1, 1), Status = EmployeeStatus.Active }; db.Employees.Add(employee); await db.SaveChangesAsync();
        var service = new ReimbursementService(db, tenant, TimeProvider.System);
        var category = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "MEAL", Name = "Meals", CategoryType = ReimbursementCategoryType.Meal, CurrencyCode = "INR", RequiresReceipt = false }); Assert.True(category.Succeeded, category.Message);
        var version = await service.CreatePolicyVersionAsync(category.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), PerTransactionLimit = 500, TaxTreatment = ReimbursementTaxTreatment.PartiallyTaxable }); Assert.True(version.Succeeded, version.Message);
        var claim = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employee.Id, ClaimDate = new DateOnly(2026, 9, 21), CurrencyCode = "INR", Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 20), Description = "Provider meal", ClaimedAmount = 100 }, new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 21), Description = "Provider meal two", ClaimedAmount = 450 }] }); Assert.True(claim.Succeeded, claim.Message);
        var attachment = await service.AddAttachmentAsync(claim.Value!.Id, new ReimbursementAttachmentRequest { ClaimLineId = claim.Value.Lines[0].Id, FileName = "meal.pdf", ContentType = "application/pdf", StorageReference = "provider/meal", FileSize = 100 }); Assert.True(attachment.Succeeded, attachment.Message);
        Assert.True((await service.SubmitAsync(claim.Value.Id)).Succeeded);
        var approved = await service.ApproveAsync(claim.Value.Id, new ReimbursementApprovalRequest { Lines = [new() { ClaimLineId = claim.Value.Lines[0].Id, ApprovedAmount = 80, TaxableAmount = 40, Comment = "Partial first line" }, new() { ClaimLineId = claim.Value.Lines[1].Id, ApprovedAmount = 300, TaxableAmount = 100, Comment = "Partial second line" }] }); Assert.True(approved.Succeeded, approved.Message); Assert.Equal(ReimbursementClaimStatus.ReadyForSettlement, approved.Value!.Status); Assert.Equal(380m, approved.Value.TotalApprovedAmount); Assert.Equal(140m, approved.Value.TaxableAmount); Assert.Equal(240m, approved.Value.NonTaxableAmount);
        var payrollSettlement = await service.RecordPayrollSettlementAsync(claim.Value.Id, claim.Value.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), 80, new DateOnly(2026, 9, 30)); Assert.True(payrollSettlement.Succeeded, payrollSettlement.Message);
        var manualSettlement = await service.SettleManuallyAsync(claim.Value.Id, new ReimbursementSettlementRequest { Amount = 300, Reference = "provider-manual-settlement" }); Assert.True(manualSettlement.Succeeded, manualSettlement.Message); Assert.Equal(ReimbursementClaimStatus.Settled, manualSettlement.Value!.Status);
        var repeat = await service.GetClaimAsync(claim.Value.Id); Assert.True(repeat.Succeeded); Assert.Equal(2, repeat.Value!.Settlements.Count); Assert.Equal(380m, repeat.Value.SettledAmount);

        var finalCategory = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "FINAL", Name = "Final settlement reimbursement", CategoryType = ReimbursementCategoryType.Other, CurrencyCode = "INR", DefaultSettlementMethod = ReimbursementSettlementMethod.FinalSettlement }); Assert.True(finalCategory.Succeeded, finalCategory.Message); Assert.True((await service.CreatePolicyVersionAsync(finalCategory.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), SettlementMethod = ReimbursementSettlementMethod.FinalSettlement })).Succeeded);
        var finalClaim = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employee.Id, ClaimDate = new DateOnly(2026, 9, 21), CurrencyCode = "INR", SettlementMethod = ReimbursementSettlementMethod.FinalSettlement, Lines = [new() { ReimbursementCategoryId = finalCategory.Value.Id, ExpenseDate = new DateOnly(2026, 9, 21), Description = "Final settlement claim", ClaimedAmount = 125 }] }); Assert.True(finalClaim.Succeeded, finalClaim.Message); Assert.True((await service.SubmitAsync(finalClaim.Value!.Id)).Succeeded); Assert.True((await service.ApproveAsync(finalClaim.Value.Id, new ReimbursementApprovalRequest { Lines = [new() { ClaimLineId = finalClaim.Value.Lines[0].Id, ApprovedAmount = 125 }] })).Succeeded);
        var finalService = new PayrollRetroSettlementService(db, tenant, TimeProvider.System); var finalCase = await finalService.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = employee.Id, SeparationDate = new DateOnly(2026, 12, 31), LastWorkingDate = new DateOnly(2026, 12, 31), SettlementDate = new DateOnly(2026, 12, 31), CurrencyCode = "INR" }); Assert.True(finalCase.Succeeded, finalCase.Message); Assert.True((await finalService.CalculateSettlementAsync(finalCase.Value!.Id)).Succeeded); Assert.True((await finalService.ApproveSettlementAsync(finalCase.Value.Id)).Succeeded); var finalized = await finalService.FinalizeSettlementAsync(finalCase.Value.Id); Assert.True(finalized.Succeeded, finalized.Message); Assert.Single(await db.ReimbursementSettlements.Where(x => x.ReimbursementClaimLineId == finalClaim.Value.Lines[0].Id && x.FinalSettlementId == finalCase.Value.Id).ToListAsync());
        tenant.TenantId = Guid.NewGuid(); Assert.Empty(await db.ReimbursementClaims.AsNoTracking().ToListAsync());
    }
}
