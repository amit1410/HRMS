using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Leave;

public sealed class LeaveTypeQuery : PagedQuery
{
    public bool? IsActive { get; set; }
}

public sealed class LeaveTypeRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LeaveUnit DefaultUnit { get; set; } = LeaveUnit.Day;
    public bool IsPaid { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ConcurrencyToken { get; set; }
}

public sealed record LeaveTypeDto(
    Guid Id, string Code, string Name, string? Description, LeaveUnit DefaultUnit, bool IsPaid,
    bool IsActive, DateTime CreatedDate, DateTime? ModifiedDate, string ConcurrencyToken);

public sealed class LeavePeriodQuery : PagedQuery
{
    public bool? IsActive { get; set; }
    public DateOnly? OnDate { get; set; }
}

public sealed class LeavePeriodRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ConcurrencyToken { get; set; }
}

public sealed record LeavePeriodDto(
    Guid Id, string Code, string Name, DateOnly StartDate, DateOnly EndDate, bool IsActive,
    DateTime CreatedDate, DateTime? ModifiedDate, string ConcurrencyToken);

public sealed class LeavePolicyRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ConcurrencyToken { get; set; }
}

public sealed class LeavePolicyQuery : PagedQuery
{
    public bool? IsActive { get; set; }
}

public sealed record LeavePolicyDto(
    Guid Id, string Code, string Name, string? Description, bool IsActive,
    int VersionCount, int? CurrentVersionNumber, DateTime CreatedDate, DateTime? ModifiedDate,
    string ConcurrencyToken);

public sealed class LeavePolicyVersionRequest
{
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int Priority { get; set; }
    public Guid? CopyFromVersionId { get; set; }
}

public sealed class LeavePolicyVersionUpdateRequest
{
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int Priority { get; set; }
    public string? ConcurrencyToken { get; set; }
}

public sealed record LeavePolicyVersionDto(
    Guid Id, int VersionNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    LeavePolicyVersionStatus Status, int Priority, int LeaveTypeCount, int ApplicabilityGroupCount,
    DateTime CreatedDate, string? CreatedBy, DateTime? ModifiedDate, string ConcurrencyToken,
    LeavePolicyAllowedActions AllowedActions);

public sealed record LeavePolicyAllowedActions(
    bool CanEdit, bool CanValidate, bool CanPublish, bool CanRetire, bool CanCreateVersion);

public sealed record LeaveTypeSelectionDto(Guid Id, string Code, string Name, bool IsActive);

public sealed class LeaveTypeSelectionRequest
{
    public IReadOnlyList<Guid> LeaveTypeIds { get; set; } = [];
    public string? ConcurrencyToken { get; set; }
}

public sealed class LeaveApplicabilityGroupRequest
{
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
}

public sealed class LeaveApplicabilityRequest
{
    public IReadOnlyList<LeaveApplicabilityGroupRequest> Groups { get; set; } = [];
    public string? ConcurrencyToken { get; set; }
}

public sealed record LeaveApplicabilityGroupDto(
    Guid Id, HRMS.Domain.Enums.Gender? Gender, Guid? HoldingCompanyId, Guid? LobId,
    Guid? OrganisationId, Guid? DepartmentId, Guid? SubDepartmentId, Guid? SectionId,
    Guid? SubSectionId, Guid? FunctionId, Guid? SubFunctionId, Guid? GradeId, Guid? DesignationId,
    Guid? EmployeeTypeId, Guid? CountryLocationId, Guid? WorkLocationId, Guid? CostCenterId);

public sealed record LeavePolicyValidationDto(bool IsValid, IReadOnlyList<ValidationError> Errors, IReadOnlyList<string> Warnings);

public sealed record LeavePolicyEditorDto(
    LeavePolicyDto Policy, LeavePolicyVersionDto? CurrentVersion,
    IReadOnlyList<LeaveTypeSelectionDto> LeaveTypes,
    IReadOnlyList<LeaveApplicabilityGroupDto> ApplicabilityGroups);

public sealed class LeavePolicyEligibilityRuleRequest
{
    public EligibilityMode EligibilityMode { get; set; } = EligibilityMode.Immediate;
    public int? MinimumServiceValue { get; set; }
    public EligibilityServiceUnit? MinimumServiceUnit { get; set; }
    public ProbationMode ProbationMode { get; set; } = ProbationMode.Allowed;
    public NoticePeriodMode NoticePeriodMode { get; set; } = NoticePeriodMode.Allowed;
    public string? ConcurrencyToken { get; set; }
}

public sealed record LeavePolicyEligibilityRuleDto(
    Guid Id, Guid LeavePolicyRuleId, EligibilityMode EligibilityMode, int? MinimumServiceValue,
    EligibilityServiceUnit? MinimumServiceUnit, ProbationMode ProbationMode,
    NoticePeriodMode NoticePeriodMode, string ConcurrencyToken);

public sealed class LeavePolicyEntitlementRuleRequest
{
    public EntitlementMode EntitlementMode { get; set; } = EntitlementMode.Allocated;
    public EntitlementSource EntitlementSource { get; set; } = EntitlementSource.PolicyAccrual;
    public decimal? EntitlementQuantity { get; set; }
    public AccrualFrequency AccrualFrequency { get; set; } = AccrualFrequency.None;
    public AccrualTiming? AccrualTiming { get; set; }
    public string? ConcurrencyToken { get; set; }
}

public sealed record LeavePolicyEntitlementRuleDto(
    Guid Id, Guid LeavePolicyRuleId, EntitlementMode EntitlementMode, EntitlementSource EntitlementSource,
    decimal? EntitlementQuantity, AccrualFrequency AccrualFrequency, AccrualTiming? AccrualTiming,
    string ConcurrencyToken);

public sealed class LeavePolicyRequestRuleRequest
{
    public decimal? MinimumRequestQuantity { get; set; }
    public decimal? MaximumRequestQuantity { get; set; }
    public decimal? MaximumConsecutiveQuantity { get; set; }
    public int MinimumAdvanceNoticeDays { get; set; }
    public BackdatedRequestMode BackdatedRequestMode { get; set; } = BackdatedRequestMode.NotAllowed;
    public int? MaximumBackdatedDays { get; set; }
    public int? MaximumRequestsPerPeriod { get; set; }
    public decimal? MaximumQuantityPerPeriod { get; set; }
    public RequestLimitPeriod? RequestLimitPeriod { get; set; }
    public PartialDayMode PartialDayMode { get; set; } = PartialDayMode.FullDayOnly;
    public string? ConcurrencyToken { get; set; }
}

public sealed record LeavePolicyRequestRuleDto(Guid Id, Guid LeavePolicyRuleId, decimal? MinimumRequestQuantity,
    decimal? MaximumRequestQuantity, decimal? MaximumConsecutiveQuantity, int MinimumAdvanceNoticeDays,
    BackdatedRequestMode BackdatedRequestMode, int? MaximumBackdatedDays, int? MaximumRequestsPerPeriod,
    decimal? MaximumQuantityPerPeriod, RequestLimitPeriod? RequestLimitPeriod, PartialDayMode PartialDayMode,
    string ConcurrencyToken);

public sealed class LeavePolicyCalendarRuleRequest
{
    public HolidayTreatment HolidayTreatment { get; set; } = HolidayTreatment.Exclude;
    public WeekOffTreatment WeekOffTreatment { get; set; } = WeekOffTreatment.Exclude;
    public SandwichMode SandwichMode { get; set; } = SandwichMode.Disabled;
    public bool ApplyToPrefix { get; set; }
    public bool ApplyToSuffix { get; set; }
    public bool ApplyToBetween { get; set; }
    public string? ConcurrencyToken { get; set; }
}
public sealed record LeavePolicyCalendarRuleDto(Guid Id, Guid LeavePolicyRuleId, HolidayTreatment HolidayTreatment, WeekOffTreatment WeekOffTreatment, SandwichMode SandwichMode, bool ApplyToPrefix, bool ApplyToSuffix, bool ApplyToBetween, string ConcurrencyToken);

public sealed class LeavePolicyAttachmentRuleRequest
{
    public AttachmentRequirement AttachmentRequirement { get; set; } = AttachmentRequirement.None;
    public decimal? ThresholdQuantity { get; set; }
    public string? DocumentLabel { get; set; }
    public string? ConcurrencyToken { get; set; }
}
public sealed record LeavePolicyAttachmentRuleDto(Guid Id, Guid LeavePolicyRuleId, AttachmentRequirement AttachmentRequirement, decimal? ThresholdQuantity, string? DocumentLabel, string ConcurrencyToken);
public sealed record LeavePolicyClubbingRuleDto(Guid Id, Guid LeavePolicyVersionId, Guid LeaveTypeAId, Guid LeaveTypeBId, ClubbingRelation Relation);
public sealed class LeavePolicyClubbingRuleRequest { public Guid LeaveTypeAId { get; set; } public Guid LeaveTypeBId { get; set; } public ClubbingRelation Relation { get; set; } = ClubbingRelation.NotAllowed; }
public sealed class LeavePolicyClubbingRequest { public List<LeavePolicyClubbingRuleRequest> Rules { get; set; } = []; public string? ConcurrencyToken { get; set; } }
public sealed class LeavePolicyCancellationRuleRequest { public bool WithdrawAllowed { get; set; } public bool CancelAllowed { get; set; } public bool ModifyAllowed { get; set; } public string? ConcurrencyToken { get; set; } }
public sealed record LeavePolicyCancellationRuleDto(Guid Id, Guid LeavePolicyRuleId, bool WithdrawAllowed, bool CancelAllowed, bool ModifyAllowed, string ConcurrencyToken);
