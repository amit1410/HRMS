using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class ReimbursementPolicyResolver(IHrmsDbContext db, ITenantContext tenant) : IReimbursementPolicyResolver
{
    public Task<ReimbursementPolicyVersion?> ResolveAsync(Guid categoryId, DateOnly expenseDate, CancellationToken ct = default) =>
        db.ReimbursementPolicyVersions.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.ReimbursementCategoryId == categoryId && x.Status == ReimbursementPolicyVersionStatus.Active && x.EffectiveFrom <= expenseDate && (x.EffectiveTo == null || x.EffectiveTo >= expenseDate)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
}

public sealed class ReimbursementEligibilityService(IHrmsDbContext db, IReimbursementPolicyResolver policies, ITenantContext tenant) : IReimbursementEligibilityService
{
    public async Task<Result<ReimbursementEligibilityResult>> EvaluateAsync(Guid employeeId, Guid categoryId, decimal amount, DateOnly expenseDate, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<ReimbursementEligibilityResult>.Unauthorized("No authenticated tenant.");
        if (amount <= 0) return Result<ReimbursementEligibilityResult>.Invalid("amount", "Claim amount must be positive.");
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, ct); if (employee is null) return Result<ReimbursementEligibilityResult>.NotFound("Employee not found.");
        if (employee.Status != EmployeeStatus.Active) return Result<ReimbursementEligibilityResult>.Success(new(false, 0, ["Employee is not active."], [], null));
        var category = await db.ReimbursementCategories.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == categoryId && x.IsActive, ct); if (category is null) return Result<ReimbursementEligibilityResult>.NotFound("Reimbursement category not found.");
        var policy = await policies.ResolveAsync(categoryId, expenseDate, ct); if (policy is null) return Result<ReimbursementEligibilityResult>.Success(new(false, 0, ["No active reimbursement policy applies to the expense date."], [], null));
        var eligible = Math.Min(amount, policy.PerTransactionLimit ?? policy.MaxClaimAmount ?? amount); var issues = new List<string>(); if (policy.MinClaimAmount is decimal minimum && amount < minimum) { eligible = 0; issues.Add("Claim is below the policy minimum."); }
        return Result<ReimbursementEligibilityResult>.Success(new(issues.Count == 0 && eligible > 0, eligible, issues, [], policy.Id));
    }
}
