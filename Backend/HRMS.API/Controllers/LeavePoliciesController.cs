using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-policies"), Produces("application/json")]
public sealed class LeavePoliciesController : ControllerBase
{
    private readonly ILeaveConfigurationService _service;
    public LeavePoliciesController(ILeaveConfigurationService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeavePolicyDto>>>> GetAll([FromQuery] LeavePolicyQuery query, CancellationToken ct) => (await _service.GetPoliciesAsync(query, ct)).ToActionResult();

    [HttpGet("{policyId:guid}"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyDto>>> Get(Guid policyId, CancellationToken ct) => (await _service.GetPolicyAsync(policyId, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyDto>>> Create([FromBody] LeavePolicyRequest request, CancellationToken ct) => (await _service.CreatePolicyAsync(request, ct)).ToCreatedResult(nameof(Get), x => new { policyId = x.Id });

    [HttpPut("{policyId:guid}"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyDto>>> Update(Guid policyId, [FromBody] LeavePolicyRequest request, CancellationToken ct) => (await _service.UpdatePolicyAsync(policyId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/editor"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyEditorDto>>> Editor(Guid policyId, [FromQuery] Guid? versionId, CancellationToken ct) => (await _service.GetEditorAsync(policyId, versionId, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeavePolicyVersionDto>>>> Versions(Guid policyId, CancellationToken ct) => (await _service.GetVersionsAsync(policyId, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyVersionDto>>> Version(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.GetVersionAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPost("{policyId:guid}/versions"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyVersionDto>>> CreateVersion(Guid policyId, [FromBody] LeavePolicyVersionRequest request, CancellationToken ct) => (await _service.CreateVersionAsync(policyId, request, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyVersionDto>>> UpdateVersion(Guid policyId, Guid versionId, [FromBody] LeavePolicyVersionUpdateRequest request, CancellationToken ct) => (await _service.UpdateVersionAsync(policyId, versionId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/leave-types"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveTypeSelectionDto>>>> LeaveTypes(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.GetVersionLeaveTypesAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/leave-types"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveTypeSelectionDto>>>> SetLeaveTypes(Guid policyId, Guid versionId, [FromBody] LeaveTypeSelectionRequest request, CancellationToken ct) => (await _service.SetVersionLeaveTypesAsync(policyId, versionId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/applicability"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveApplicabilityGroupDto>>>> Applicability(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.GetApplicabilityAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/applicability"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveApplicabilityGroupDto>>>> SetApplicability(Guid policyId, Guid versionId, [FromBody] LeaveApplicabilityRequest request, CancellationToken ct) => (await _service.SetApplicabilityAsync(policyId, versionId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/eligibility"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyEligibilityRuleDto?>>> Eligibility(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct) => (await _service.GetEligibilityAsync(policyId, versionId, leaveTypeId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/eligibility"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyEligibilityRuleDto?>>> SetEligibility(Guid policyId, Guid versionId, Guid leaveTypeId, [FromBody] LeavePolicyEligibilityRuleRequest request, CancellationToken ct) => (await _service.SaveEligibilityAsync(policyId, versionId, leaveTypeId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/entitlement"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyEntitlementRuleDto?>>> Entitlement(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct) => (await _service.GetEntitlementAsync(policyId, versionId, leaveTypeId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/entitlement"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyEntitlementRuleDto?>>> SetEntitlement(Guid policyId, Guid versionId, Guid leaveTypeId, [FromBody] LeavePolicyEntitlementRuleRequest request, CancellationToken ct) => (await _service.SaveEntitlementAsync(policyId, versionId, leaveTypeId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/request-rules"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyRequestRuleDto?>>> RequestRules(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct) => (await _service.GetRequestRuleAsync(policyId, versionId, leaveTypeId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/request-rules"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyRequestRuleDto?>>> SetRequestRules(Guid policyId, Guid versionId, Guid leaveTypeId, [FromBody] LeavePolicyRequestRuleRequest request, CancellationToken ct) => (await _service.SaveRequestRuleAsync(policyId, versionId, leaveTypeId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/calendar"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyCalendarRuleDto?>>> Calendar(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct) => (await _service.GetCalendarRuleAsync(policyId, versionId, leaveTypeId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/calendar"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyCalendarRuleDto?>>> SetCalendar(Guid policyId, Guid versionId, Guid leaveTypeId, [FromBody] LeavePolicyCalendarRuleRequest request, CancellationToken ct) => (await _service.SaveCalendarRuleAsync(policyId, versionId, leaveTypeId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/attachments"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyAttachmentRuleDto?>>> Attachments(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct) => (await _service.GetAttachmentRuleAsync(policyId, versionId, leaveTypeId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/attachments"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyAttachmentRuleDto?>>> SetAttachments(Guid policyId, Guid versionId, Guid leaveTypeId, [FromBody] LeavePolicyAttachmentRuleRequest request, CancellationToken ct) => (await _service.SaveAttachmentRuleAsync(policyId, versionId, leaveTypeId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/clubbing"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeavePolicyClubbingRuleDto>>>> Clubbing(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.GetClubbingAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/clubbing"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeavePolicyClubbingRuleDto>>>> SetClubbing(Guid policyId, Guid versionId, [FromBody] LeavePolicyClubbingRequest request, CancellationToken ct) => (await _service.SaveClubbingAsync(policyId, versionId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/cancellation"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePolicyCancellationRuleDto?>>> Cancellation(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct) => (await _service.GetCancellationRuleAsync(policyId, versionId, leaveTypeId, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}/leave-types/{leaveTypeId:guid}/cancellation"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyCancellationRuleDto?>>> SetCancellation(Guid policyId, Guid versionId, Guid leaveTypeId, [FromBody] LeavePolicyCancellationRuleRequest request, CancellationToken ct) => (await _service.SaveCancellationRuleAsync(policyId, versionId, leaveTypeId, request, ct)).ToActionResult();

    [HttpPost("{policyId:guid}/versions/{versionId:guid}/validate"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<LeavePolicyValidationDto>>> Validate(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.ValidateAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPost("{policyId:guid}/versions/{versionId:guid}/publish"), HasPermission(Permissions.Leave.PolicyPublish)]
    public async Task<ActionResult<ApiResponse<LeavePolicyVersionDto>>> Publish(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.PublishAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPost("{policyId:guid}/versions/{versionId:guid}/retire"), HasPermission(Permissions.Leave.PolicyPublish)]
    public async Task<ActionResult<ApiResponse<LeavePolicyVersionDto>>> Retire(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.RetireAsync(policyId, versionId, ct)).ToActionResult();
}
