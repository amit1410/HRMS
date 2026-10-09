using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public enum ConfirmationPolicyResolutionStatus
{
    Resolved,
    NoPolicy,
    InvalidTenant,
    EffectiveEmploymentNotFound,
    EffectiveEmploymentAmbiguous,
    ConfigurationAmbiguity
}

/// <summary>
/// The applicable confirmation/probation policy for an employee on a date, plus the probation parameters
/// needed to compute the probation-end / confirmation date. Mirrors the leave resolver result shape.
/// </summary>
public sealed record ConfirmationPolicyResolutionResult(
    ConfirmationPolicyResolutionStatus Status,
    Guid TenantId,
    Guid EmployeeId,
    DateOnly EffectiveDate,
    Guid? ConfirmationPolicyId,
    Guid? ConfirmationPolicyVersionId,
    int? Priority,
    int? Specificity,
    int? ProbationDuration,
    ProbationDurationUnit? ProbationDurationUnit,
    int? TriggerDaysBeforeEnd,
    int? MaxExtensions,
    int? ExtensionDuration,
    ProbationDurationUnit? ExtensionDurationUnit,
    DateOnly? DateOfJoining,
    DateOnly? ProbationEndDate,
    string Message);

public interface IConfirmationPolicyResolver
{
    Task<ConfirmationPolicyResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, DateOnly effectiveDate, CancellationToken ct = default);
}
