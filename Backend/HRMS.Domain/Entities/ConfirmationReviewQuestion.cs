using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A tenant-configurable confirmation review question — the master from which Phase 9c-2 review forms are
/// rendered. Questions are entirely data-driven (text, type, mandatory, ordering, effective dates) and are
/// never hard-coded. A <see cref="ReviewQuestionType.Rating"/> question references a configured
/// <see cref="ConfirmationRatingScale"/>; <see cref="ReviewQuestionType.Dropdown"/>/
/// <see cref="ReviewQuestionType.MultiSelect"/> questions carry configured
/// <see cref="ConfirmationReviewQuestionOption"/>s. Applicability (<see cref="ApplicabilitySets"/>) reuses
/// the confirmation-policy applicability dimensions so the existing priority/specificity conventions apply
/// unchanged when the review case is assembled later.
/// <para>
/// Review <em>answers</em> are never stored here or on the employee record; this is configuration only.
/// </para>
/// </summary>
public sealed class ConfirmationReviewQuestion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>Optional help/description text shown with the question.</summary>
    public string? HelpText { get; set; }

    public ReviewQuestionType QuestionType { get; set; } = ReviewQuestionType.Rating;
    public bool IsMandatory { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>The rating scale this question uses. Required for <see cref="ReviewQuestionType.Rating"/>, otherwise null.</summary>
    public Guid? ConfirmationRatingScaleId { get; set; }

    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }

    public Tenant? Tenant { get; set; }
    public ConfirmationRatingScale? ConfirmationRatingScale { get; set; }
    public ICollection<ConfirmationReviewQuestionOption> Options { get; set; } = new List<ConfirmationReviewQuestionOption>();
    public ICollection<ConfirmationReviewQuestionApplicabilitySet> ApplicabilitySets { get; set; } = new List<ConfirmationReviewQuestionApplicabilitySet>();
}

/// <summary>
/// A selectable option for a <see cref="ReviewQuestionType.Dropdown"/> or
/// <see cref="ReviewQuestionType.MultiSelect"/> review question. Options are configured per question and
/// are not hard-coded.
/// </summary>
public sealed class ConfirmationReviewQuestionOption : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewQuestionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ConfirmationReviewQuestion? ConfirmationReviewQuestion { get; set; }
}

/// <summary>
/// One applicability row that scopes a review question to employees. Optionally bound to a specific
/// <see cref="ConfirmationPolicy"/> and/or to the usual employment dimensions (identical to
/// <see cref="ConfirmationPolicyApplicabilitySet"/>) so the existing matching/specificity logic is reused
/// verbatim. A row with no populated dimension and no policy is the tenant-wide default (specificity 0).
/// </summary>
public sealed class ConfirmationReviewQuestionApplicabilitySet : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewQuestionId { get; set; }

    /// <summary>Optional association to a specific confirmation policy (null = any policy).</summary>
    public Guid? ConfirmationPolicyId { get; set; }

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

    public ConfirmationReviewQuestion? ConfirmationReviewQuestion { get; set; }
    public ConfirmationPolicy? ConfirmationPolicy { get; set; }
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
