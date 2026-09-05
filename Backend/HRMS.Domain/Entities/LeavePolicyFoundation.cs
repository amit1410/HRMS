using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class LeaveType : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LeaveUnit DefaultUnit { get; set; } = LeaveUnit.Day;
    public bool IsPaid { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<LeavePolicyRule> PolicyRules { get; set; } = new List<LeavePolicyRule>();
}

public sealed class LeavePeriod : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public Tenant? Tenant { get; set; }
}

public sealed class LeavePolicy : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<LeavePolicyVersion> Versions { get; set; } = new List<LeavePolicyVersion>();
}

public sealed class LeavePolicyVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public int VersionNumber { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public LeavePolicyVersionStatus Status { get; set; } = LeavePolicyVersionStatus.Draft;
    public int Priority { get; set; }
    public string? CreatedBy { get; set; }
    public LeavePolicy? LeavePolicy { get; set; }
    public ICollection<LeavePolicyRule> Rules { get; set; } = new List<LeavePolicyRule>();
    public ICollection<LeavePolicyApplicabilitySet> ApplicabilitySets { get; set; } = new List<LeavePolicyApplicabilitySet>();
}

public sealed class LeavePolicyRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyVersionId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public bool IsActive { get; set; } = true;
    public LeavePolicyVersion? LeavePolicyVersion { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeavePolicyEligibilityRule? EligibilityRule { get; set; }
    public LeavePolicyEntitlementRule? EntitlementRule { get; set; }
    public LeavePolicyRequestRule? RequestRule { get; set; }
    public LeavePolicyCalendarRule? CalendarRule { get; set; }
    public LeavePolicyAttachmentRule? AttachmentRule { get; set; }
    public ICollection<LeavePolicyClubbingRule> ClubbingRulesAsLower { get; set; } = new List<LeavePolicyClubbingRule>();
    public ICollection<LeavePolicyClubbingRule> ClubbingRulesAsHigher { get; set; } = new List<LeavePolicyClubbingRule>();
    public LeavePolicyCancellationRule? CancellationRule { get; set; }
}

public sealed class LeavePolicyEligibilityRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public EligibilityMode EligibilityMode { get; set; } = EligibilityMode.Immediate;
    public int? MinimumServiceValue { get; set; }
    public EligibilityServiceUnit? MinimumServiceUnit { get; set; }
    public ProbationMode ProbationMode { get; set; } = ProbationMode.Allowed;
    public NoticePeriodMode NoticePeriodMode { get; set; } = NoticePeriodMode.Allowed;
    public LeavePolicyRule? LeavePolicyRule { get; set; }
}

public sealed class LeavePolicyEntitlementRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public EntitlementMode EntitlementMode { get; set; } = EntitlementMode.Allocated;
    public EntitlementSource EntitlementSource { get; set; } = EntitlementSource.PolicyAccrual;
    public decimal? EntitlementQuantity { get; set; }
    public AccrualFrequency AccrualFrequency { get; set; } = AccrualFrequency.None;
    public AccrualTiming? AccrualTiming { get; set; }
    public LeavePolicyRule? LeavePolicyRule { get; set; }
}

public sealed class LeavePolicyRequestRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
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
    public LeavePolicyRule? LeavePolicyRule { get; set; }
}

public sealed class LeavePolicyCalendarRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public HolidayTreatment HolidayTreatment { get; set; } = HolidayTreatment.Exclude;
    public WeekOffTreatment WeekOffTreatment { get; set; } = WeekOffTreatment.Exclude;
    public SandwichMode SandwichMode { get; set; } = SandwichMode.Disabled;
    public bool ApplyToPrefix { get; set; }
    public bool ApplyToSuffix { get; set; }
    public bool ApplyToBetween { get; set; }
    public LeavePolicyRule? LeavePolicyRule { get; set; }
}

public sealed class LeavePolicyAttachmentRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public AttachmentRequirement AttachmentRequirement { get; set; } = AttachmentRequirement.None;
    public decimal? ThresholdQuantity { get; set; }
    public string? DocumentLabel { get; set; }
    public LeavePolicyRule? LeavePolicyRule { get; set; }
}

public sealed class LeavePolicyClubbingRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyVersionId { get; set; }
    public Guid LowerLeavePolicyRuleId { get; set; }
    public Guid HigherLeavePolicyRuleId { get; set; }
    public ClubbingRelation Relation { get; set; } = ClubbingRelation.NotAllowed;
    public LeavePolicyVersion? LeavePolicyVersion { get; set; }
    public LeavePolicyRule? LowerLeavePolicyRule { get; set; }
    public LeavePolicyRule? HigherLeavePolicyRule { get; set; }
}

public sealed class LeavePolicyCancellationRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public bool WithdrawAllowed { get; set; }
    public bool CancelAllowed { get; set; }
    public bool ModifyAllowed { get; set; }
    public LeavePolicyRule? LeavePolicyRule { get; set; }
}

public sealed class LeavePolicyApplicabilitySet : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeavePolicyVersionId { get; set; }
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
    public LeavePolicyVersion? LeavePolicyVersion { get; set; }
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
