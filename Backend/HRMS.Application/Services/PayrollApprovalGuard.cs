using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollApprovalGuard(IHrmsDbContext db, ITenantContext tenant) : IPayrollApprovalGuard
{
    public async Task<Result<bool>> ValidateAsync(Guid? makerUserId, string action, string? reason = null, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<bool>.Unauthorized("No authenticated tenant.");
        var policy = await db.PayrollControlConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (policy is null) return Result<bool>.Success(true);
        if (policy.RequireMakerChecker && policy.PreventSelfApproval && makerUserId.HasValue && tenant.UserId.HasValue && makerUserId == tenant.UserId)
            return Result<bool>.Invalid("approval", "SelfApprovalNotAllowed: the user who created this payroll action cannot approve it.");
        if (policy.RequireReasonForReopen && action.Equals("reopen", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(reason))
            return Result<bool>.Invalid("reason", "A reopen reason is required.");
        if (policy.RequireReasonForCancellation && action.Equals("cancel", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(reason))
            return Result<bool>.Invalid("reason", "A cancellation reason is required.");
        return Result<bool>.Success(true);
    }
}
