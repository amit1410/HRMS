using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A tenant-configurable confirmation &amp; probation policy. Mirrors the leave-policy shape
/// (<see cref="LeavePolicy"/>): a policy owns effective-dated <see cref="ConfirmationPolicyVersion"/>s,
/// each of which carries one probation <see cref="ConfirmationPolicyRule"/> and zero or more
/// <see cref="ConfirmationPolicyApplicabilitySet"/>s. The applicable policy for an employee is resolved
/// by priority then specificity (see ConfirmationPolicyResolver), so the Confirmation Date is derived
/// from policy rather than maintained by hand on the employment record.
/// </summary>
public sealed class ConfirmationPolicy : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<ConfirmationPolicyVersion> Versions { get; set; } = new List<ConfirmationPolicyVersion>();
}

public sealed class ConfirmationPolicyVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationPolicyId { get; set; }
    public int VersionNumber { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public ConfirmationPolicyVersionStatus Status { get; set; } = ConfirmationPolicyVersionStatus.Draft;

    /// <summary>Higher wins when multiple policies apply; ties are broken by applicability specificity.</summary>
    public int Priority { get; set; }
    public string? CreatedBy { get; set; }

    public ConfirmationPolicy? ConfirmationPolicy { get; set; }
    public ConfirmationPolicyRule? Rule { get; set; }
    public ICollection<ConfirmationPolicyApplicabilitySet> ApplicabilitySets { get; set; } = new List<ConfirmationPolicyApplicabilitySet>();

    /// <summary>Phase 9c-3 decision rules that map an overall rating range to an outcome for this version.</summary>
    public ICollection<ConfirmationDecisionRule> DecisionRules { get; set; } = new List<ConfirmationDecisionRule>();
}

/// <summary>
/// The probation configuration for a version (1:1). Confirmation Date is computed as
/// Date of Joining + <see cref="ProbationDuration"/>; the scheduler (Phase 9b) triggers
/// <see cref="TriggerDaysBeforeEnd"/> days before that date. Review/decision fields are
/// reserved for the Phase 9c workflow and are nullable so 9a schema stays stable.
/// </summary>
public sealed class ConfirmationPolicyRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationPolicyVersionId { get; set; }

    /// <summary>Probation length (e.g. 3/6/12). Interpreted with <see cref="ProbationDurationUnit"/>.</summary>
    public int ProbationDuration { get; set; }
    public ProbationDurationUnit ProbationDurationUnit { get; set; } = ProbationDurationUnit.Months;

    /// <summary>Days before probation end that the scheduler notifies/raises the review (e.g. 7/15/30).</summary>
    public int TriggerDaysBeforeEnd { get; set; }

    /// <summary>Maximum number of times probation may be extended before a terminal decision is required.</summary>
    public int MaxExtensions { get; set; }

    /// <summary>Length of each probation extension.</summary>
    public int ExtensionDuration { get; set; }
    public ProbationDurationUnit ExtensionDurationUnit { get; set; } = ProbationDurationUnit.Months;

    // --- Phase 9c decision workflow. Nullable/defaulted so 9a/9b rows migrate unchanged. ---

    /// <summary>Which reviewer reviews must be submitted before a case is Completed / a decision may be made.</summary>
    public ConfirmationApprovalRequirement RequiredApproval { get; set; } = ConfirmationApprovalRequirement.ManagerAndHrbp;

    /// <summary>Optional convenience threshold (kept for 9a/9b compatibility); decision rules supersede it.</summary>
    public decimal? ConfirmRatingThreshold { get; set; }
    public bool AutoConfirmOnApproval { get; set; } = true;

    /// <summary>How the single overall rating is computed from the reviewer's Rating answers.</summary>
    public OverallRatingMethod OverallRatingMethod { get; set; } = OverallRatingMethod.AverageOfRatings;

    /// <summary>Ordering constraint on reviewer-review submission.</summary>
    public ApprovalSequence ApprovalSequence { get; set; } = ApprovalSequence.Parallel;

    /// <summary>When true, a decision cannot be submitted unless each required review carries overall comments.</summary>
    public bool RequireReviewerComments { get; set; }

    /// <summary>Job status stamped on the employment record when the employee is confirmed. Defaults to "Confirmed".</summary>
    public string? ConfirmedJobStatus { get; set; }

    /// <summary>Job status stamped when the employee is not confirmed/rejected. Defaults to "Not Confirmed".</summary>
    public string? NotConfirmedJobStatus { get; set; }

    public ConfirmationPolicyVersion? ConfirmationPolicyVersion { get; set; }
}

/// <summary>
/// A tenant-configurable decision rule for a confirmation policy version (Phase 9c-3). Each rule maps an
/// inclusive overall-rating range <c>[<see cref="MinOverallRating"/>, <see cref="MaxOverallRating"/>]</c> to a
/// <see cref="ConfirmationOutcome"/>. Rules are evaluated deterministically by ascending <see cref="Priority"/>
/// (then <see cref="BaseEntity.Id"/>); the first active rule whose range contains the calculated overall rating
/// wins. A null bound means "unbounded on that side". Active rules for a version are validated to have
/// non-overlapping ranges so the match is never ambiguous. Rules live on the published (immutable) version, so
/// reading them via a case's frozen <c>ConfirmationPolicyVersionId</c> is effectively a snapshot.
/// </summary>
public sealed class ConfirmationDecisionRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationPolicyVersionId { get; set; }

    /// <summary>Lower priority number is evaluated first. Deterministic tie-break is by <see cref="BaseEntity.Id"/>.</summary>
    public int Priority { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Inclusive lower bound of the overall rating; null = unbounded below.</summary>
    public decimal? MinOverallRating { get; set; }

    /// <summary>Inclusive upper bound of the overall rating; null = unbounded above.</summary>
    public decimal? MaxOverallRating { get; set; }

    public ConfirmationOutcome Outcome { get; set; }
    public bool IsActive { get; set; } = true;

    public ConfirmationPolicyVersion? ConfirmationPolicyVersion { get; set; }
}

/// <summary>
/// One applicability row for a version. An employee matches when every populated dimension equals the
/// employee's effective employment snapshot; specificity is the count of populated dimensions. The
/// dimension list is intentionally identical to <see cref="LeavePolicyApplicabilitySet"/> so the shared
/// EffectiveEmploymentSnapshot matching/specificity logic is reusable.
/// </summary>
public sealed class ConfirmationPolicyApplicabilitySet : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationPolicyVersionId { get; set; }
    public HRMS.Domain.Enums.Gender? Gender { get; set; }
    public Guid? HoldingCompanyId { get; set; }
    public Guid? LobId { get; set; }
    public Guid? OrganisationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? SubDepartmentId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? SubSectionId { get; set; }
    public Guid? FunctionId { get; set; }
    public Guid? SubFunctionId { get; set; }
    public Guid? GradeId { get; set; }
    public Guid? DesignationId { get; set; }
    public Guid? EmployeeTypeId { get; set; }
    public Guid? CountryLocationId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public Guid? CostCenterId { get; set; }
    public ConfirmationPolicyVersion? ConfirmationPolicyVersion { get; set; }
    public HoldingCompany? HoldingCompany { get; set; }
    public Lob? Lob { get; set; }
    public Organisation? Organisation { get; set; }
    public Department? Department { get; set; }
    public SubDepartment? SubDepartment { get; set; }
    public Section? Section { get; set; }
    public SubSection? SubSection { get; set; }
    public Function? Function { get; set; }
    public SubFunction? SubFunction { get; set; }
    public Grade? Grade { get; set; }
    public Designation? Designation { get; set; }
    public EmployeeType? EmployeeType { get; set; }
    public Country? CountryLocation { get; set; }
    public WorkLocation? WorkLocation { get; set; }
    public CostCenter? CostCenter { get; set; }
}
