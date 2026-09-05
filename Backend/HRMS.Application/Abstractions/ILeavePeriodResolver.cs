using HRMS.Application.DTOs.Leave;

namespace HRMS.Application.Abstractions;

public interface ILeavePeriodResolver
{
    Task<LeavePeriodResolutionResult> ResolveAsync(Guid tenantId, DateOnly effectiveDate, CancellationToken ct = default);
}

public enum LeavePeriodResolutionStatus
{
    Resolved,
    NotConfigured,
    ConfigurationAmbiguity,
    InvalidTenant
}

public sealed record LeavePeriodResolutionResult(
    LeavePeriodResolutionStatus Status,
    Guid TenantId,
    DateOnly EffectiveDate,
    LeavePeriodDto? Period,
    string Message);
