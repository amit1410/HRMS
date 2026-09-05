using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;

namespace HRMS.Application.Abstractions;

public interface ILeavePolicyFoundationService
{
    Task<Result<LeavePolicyVersion>> CreateDraftVersionAsync(Guid tenantId, Guid policyId, DateOnly effectiveFrom, DateOnly? effectiveTo, int priority, string? actor, CancellationToken ct = default);
    Task<Result<bool>> PublishAsync(Guid tenantId, Guid versionId, string? actor, CancellationToken ct = default);
    Task<Result<bool>> ValidatePeriodAsync(Guid tenantId, LeavePeriod period, CancellationToken ct = default);
}

public interface ILeavePolicyResolver
{
    Task<LeavePolicyResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly effectiveDate, CancellationToken ct = default);
}
