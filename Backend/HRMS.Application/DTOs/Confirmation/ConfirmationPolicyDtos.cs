using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Confirmation;

// --- Policy ---

public sealed record ConfirmationPolicyDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int VersionCount,
    int PublishedVersionCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);

public sealed class ConfirmationPolicyQuery : PagedQuery
{
    public bool? IsActive { get; set; }

    /// <summary>Supported sort fields for this endpoint (validated against this whitelist).</summary>
    public static readonly IReadOnlyList<string> SortFields = new[] { "code", "name", "isactive", "createddate" };
}

public sealed record ConfirmationPolicyRequest(
    string Code,
    string Name,
    string? Description,
    bool IsActive = true);

// --- Probation configuration (the version's rule) ---

public sealed record ConfirmationProbationConfigDto(
    int ProbationDuration,
    ProbationDurationUnit ProbationDurationUnit,
    int TriggerDaysBeforeEnd,
    int MaxExtensions,
    int ExtensionDuration,
    ProbationDurationUnit ExtensionDurationUnit,
    ConfirmationApprovalRequirement RequiredApproval,
    decimal? ConfirmRatingThreshold,
    bool AutoConfirmOnApproval,
    OverallRatingMethod OverallRatingMethod,
    ApprovalSequence ApprovalSequence,
    bool RequireReviewerComments,
    string? ConfirmedJobStatus,
    string? NotConfirmedJobStatus);

public sealed record ConfirmationProbationConfigRequest(
    int ProbationDuration,
    ProbationDurationUnit ProbationDurationUnit = ProbationDurationUnit.Months,
    int TriggerDaysBeforeEnd = 30,
    int MaxExtensions = 0,
    int ExtensionDuration = 0,
    ProbationDurationUnit ExtensionDurationUnit = ProbationDurationUnit.Months,
    ConfirmationApprovalRequirement RequiredApproval = ConfirmationApprovalRequirement.ManagerAndHrbp,
    decimal? ConfirmRatingThreshold = null,
    bool AutoConfirmOnApproval = true,
    OverallRatingMethod OverallRatingMethod = OverallRatingMethod.AverageOfRatings,
    ApprovalSequence ApprovalSequence = ApprovalSequence.Parallel,
    bool RequireReviewerComments = false,
    string? ConfirmedJobStatus = null,
    string? NotConfirmedJobStatus = null);

// --- Decision rules (Phase 9c-3; owned by the policy version, editable while Draft) ---

public sealed record ConfirmationDecisionRuleDto(
    Guid Id,
    int Priority,
    string Name,
    string? Description,
    decimal? MinOverallRating,
    decimal? MaxOverallRating,
    ConfirmationOutcome Outcome,
    bool IsActive);

public sealed record ConfirmationDecisionRuleRequest(
    int Priority,
    string Name,
    ConfirmationOutcome Outcome,
    decimal? MinOverallRating = null,
    decimal? MaxOverallRating = null,
    string? Description = null,
    bool IsActive = true);

// --- Applicability ---

public sealed record ConfirmationPolicyApplicabilityDto(
    Guid Id,
    Gender? Gender,
    Guid? HoldingCompanyId,
    Guid? LobId,
    Guid? OrganisationId,
    Guid? DepartmentId,
    Guid? SubDepartmentId,
    Guid? SectionId,
    Guid? SubSectionId,
    Guid? FunctionId,
    Guid? SubFunctionId,
    Guid? GradeId,
    Guid? DesignationId,
    Guid? EmployeeTypeId,
    Guid? CountryLocationId,
    Guid? WorkLocationId,
    Guid? CostCenterId);

public sealed record ConfirmationPolicyApplicabilityRequest(
    Gender? Gender = null,
    Guid? HoldingCompanyId = null,
    Guid? LobId = null,
    Guid? OrganisationId = null,
    Guid? DepartmentId = null,
    Guid? SubDepartmentId = null,
    Guid? SectionId = null,
    Guid? SubSectionId = null,
    Guid? FunctionId = null,
    Guid? SubFunctionId = null,
    Guid? GradeId = null,
    Guid? DesignationId = null,
    Guid? EmployeeTypeId = null,
    Guid? CountryLocationId = null,
    Guid? WorkLocationId = null,
    Guid? CostCenterId = null);

// --- Versions ---

public sealed record ConfirmationPolicyVersionDto(
    Guid Id,
    int VersionNumber,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    ConfirmationPolicyVersionStatus Status,
    int Priority,
    int ApplicabilitySetCount,
    DateTime CreatedDate,
    string? CreatedBy,
    DateTime? ModifiedDate);

public sealed record ConfirmationPolicyVersionRequest(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int Priority,
    ConfirmationProbationConfigRequest Probation,
    IReadOnlyList<ConfirmationPolicyApplicabilityRequest>? Applicability = null,
    IReadOnlyList<ConfirmationDecisionRuleRequest>? DecisionRules = null);

public sealed record ConfirmationPolicyEditorDto(
    ConfirmationPolicyDto Policy,
    ConfirmationPolicyVersionDto Version,
    ConfirmationProbationConfigDto Probation,
    IReadOnlyList<ConfirmationPolicyApplicabilityDto> Applicability,
    IReadOnlyList<ConfirmationDecisionRuleDto> DecisionRules);

// --- Resolve / employee status ---

public sealed record ConfirmationPolicyResolveRequest(Guid EmployeeId, DateOnly? EffectiveDate = null);

public sealed record ConfirmationPolicyResolveDto(
    ConfirmationPolicyResolutionStatus Status,
    Guid EmployeeId,
    DateOnly EffectiveDate,
    Guid? ConfirmationPolicyId,
    Guid? ConfirmationPolicyVersionId,
    int? Priority,
    int? Specificity,
    int? ProbationDuration,
    ProbationDurationUnit? ProbationDurationUnit,
    int? TriggerDaysBeforeEnd,
    DateOnly? DateOfJoining,
    DateOnly? ProbationEndDate,
    string Message);

/// <summary>
/// The confirmation/probation picture for one employee: policy-derived when a policy applies, otherwise the
/// employee's existing manual values with <see cref="IsPolicyManaged"/> false (so the UI keeps manual entry).
/// </summary>
public sealed record EmployeeConfirmationStatusDto(
    Guid EmployeeId,
    DateOnly DateOfJoining,
    bool IsPolicyManaged,
    Guid? ConfirmationPolicyId,
    string? ConfirmationPolicyName,
    DateOnly? ProbationEndDate,
    DateOnly? ReviewTriggerDate,
    DateOnly? ConfirmationDate,
    string? JobStatus,
    string Message);
