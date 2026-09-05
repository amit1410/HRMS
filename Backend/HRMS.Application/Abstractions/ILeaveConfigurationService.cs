using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;

namespace HRMS.Application.Abstractions;

public interface ILeaveConfigurationService
{
    Task<Result<PagedResult<LeaveTypeDto>>> GetLeaveTypesAsync(LeaveTypeQuery query, CancellationToken ct = default);
    Task<Result<LeaveTypeDto>> GetLeaveTypeAsync(Guid id, CancellationToken ct = default);
    Task<Result<LeaveTypeDto>> CreateLeaveTypeAsync(LeaveTypeRequest request, CancellationToken ct = default);
    Task<Result<LeaveTypeDto>> UpdateLeaveTypeAsync(Guid id, LeaveTypeRequest request, CancellationToken ct = default);

    Task<Result<PagedResult<LeavePeriodDto>>> GetLeavePeriodsAsync(LeavePeriodQuery query, CancellationToken ct = default);
    Task<Result<LeavePeriodDto>> GetLeavePeriodAsync(Guid id, CancellationToken ct = default);
    Task<Result<LeavePeriodDto>> CreateLeavePeriodAsync(LeavePeriodRequest request, CancellationToken ct = default);
    Task<Result<LeavePeriodDto>> UpdateLeavePeriodAsync(Guid id, LeavePeriodRequest request, CancellationToken ct = default);

    Task<Result<PagedResult<LeavePolicyDto>>> GetPoliciesAsync(LeavePolicyQuery query, CancellationToken ct = default);
    Task<Result<LeavePolicyDto>> GetPolicyAsync(Guid id, CancellationToken ct = default);
    Task<Result<LeavePolicyDto>> CreatePolicyAsync(LeavePolicyRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyDto>> UpdatePolicyAsync(Guid id, LeavePolicyRequest request, CancellationToken ct = default);

    Task<Result<PagedResult<LeavePolicyVersionDto>>> GetVersionsAsync(Guid policyId, CancellationToken ct = default);
    Task<Result<LeavePolicyVersionDto>> GetVersionAsync(Guid policyId, Guid versionId, CancellationToken ct = default);
    Task<Result<LeavePolicyEditorDto>> GetEditorAsync(Guid policyId, Guid? versionId, CancellationToken ct = default);
    Task<Result<LeavePolicyVersionDto>> CreateVersionAsync(Guid policyId, LeavePolicyVersionRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyVersionDto>> UpdateVersionAsync(Guid policyId, Guid versionId, LeavePolicyVersionUpdateRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<LeaveTypeSelectionDto>>> GetVersionLeaveTypesAsync(Guid policyId, Guid versionId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<LeaveTypeSelectionDto>>> SetVersionLeaveTypesAsync(Guid policyId, Guid versionId, LeaveTypeSelectionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<LeaveApplicabilityGroupDto>>> GetApplicabilityAsync(Guid policyId, Guid versionId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<LeaveApplicabilityGroupDto>>> SetApplicabilityAsync(Guid policyId, Guid versionId, LeaveApplicabilityRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyValidationDto>> ValidateAsync(Guid policyId, Guid versionId, CancellationToken ct = default);
    Task<Result<LeavePolicyEligibilityRuleDto?>> GetEligibilityAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default);
    Task<Result<LeavePolicyEligibilityRuleDto?>> SaveEligibilityAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyEligibilityRuleRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyEntitlementRuleDto?>> GetEntitlementAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default);
    Task<Result<LeavePolicyEntitlementRuleDto?>> SaveEntitlementAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyEntitlementRuleRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyRequestRuleDto?>> GetRequestRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default);
    Task<Result<LeavePolicyRequestRuleDto?>> SaveRequestRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyRequestRuleRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyCalendarRuleDto?>> GetCalendarRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default);
    Task<Result<LeavePolicyCalendarRuleDto?>> SaveCalendarRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyCalendarRuleRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyAttachmentRuleDto?>> GetAttachmentRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default);
    Task<Result<LeavePolicyAttachmentRuleDto?>> SaveAttachmentRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyAttachmentRuleRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>> GetClubbingAsync(Guid policyId, Guid versionId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>> SaveClubbingAsync(Guid policyId, Guid versionId, LeavePolicyClubbingRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyCancellationRuleDto?>> GetCancellationRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default);
    Task<Result<LeavePolicyCancellationRuleDto?>> SaveCancellationRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyCancellationRuleRequest request, CancellationToken ct = default);
    Task<Result<LeavePolicyVersionDto>> PublishAsync(Guid policyId, Guid versionId, CancellationToken ct = default);
    Task<Result<LeavePolicyVersionDto>> RetireAsync(Guid policyId, Guid versionId, CancellationToken ct = default);
}
