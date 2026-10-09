using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/confirmation-policies"), Produces("application/json")]
public sealed class ConfirmationPoliciesController : ControllerBase
{
    private readonly IConfirmationPolicyService _service;
    public ConfirmationPoliciesController(IConfirmationPolicyService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Confirmation.PolicyView)]
    public async Task<ActionResult<ApiResponse<PagedResult<ConfirmationPolicyDto>>>> GetAll([FromQuery] ConfirmationPolicyQuery query, CancellationToken ct) => (await _service.GetPoliciesAsync(query, ct)).ToActionResult();

    [HttpGet("{policyId:guid}"), HasPermission(Permissions.Confirmation.PolicyView)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyDto>>> Get(Guid policyId, CancellationToken ct) => (await _service.GetPolicyAsync(policyId, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Confirmation.PolicyManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyDto>>> Create([FromBody] ConfirmationPolicyRequest request, CancellationToken ct) => (await _service.CreatePolicyAsync(request, ct)).ToCreatedResult(nameof(Get), x => new { policyId = x.Id });

    [HttpPut("{policyId:guid}"), HasPermission(Permissions.Confirmation.PolicyManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyDto>>> Update(Guid policyId, [FromBody] ConfirmationPolicyRequest request, CancellationToken ct) => (await _service.UpdatePolicyAsync(policyId, request, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/versions"), HasPermission(Permissions.Confirmation.PolicyView)]
    public async Task<ActionResult<ApiResponse<PagedResult<ConfirmationPolicyVersionDto>>>> Versions(Guid policyId, CancellationToken ct) => (await _service.GetVersionsAsync(policyId, ct)).ToActionResult();

    [HttpGet("{policyId:guid}/editor"), HasPermission(Permissions.Confirmation.PolicyView)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyEditorDto>>> Editor(Guid policyId, [FromQuery] Guid? versionId, CancellationToken ct) => (await _service.GetEditorAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPost("{policyId:guid}/versions"), HasPermission(Permissions.Confirmation.PolicyManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyEditorDto>>> CreateVersion(Guid policyId, [FromBody] ConfirmationPolicyVersionRequest request, CancellationToken ct) => (await _service.CreateVersionAsync(policyId, request, ct)).ToActionResult();

    [HttpPut("{policyId:guid}/versions/{versionId:guid}"), HasPermission(Permissions.Confirmation.PolicyManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyEditorDto>>> UpdateVersion(Guid policyId, Guid versionId, [FromBody] ConfirmationPolicyVersionRequest request, CancellationToken ct) => (await _service.UpdateVersionAsync(policyId, versionId, request, ct)).ToActionResult();

    [HttpPost("{policyId:guid}/versions/{versionId:guid}/publish"), HasPermission(Permissions.Confirmation.PolicyPublish)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyVersionDto>>> Publish(Guid policyId, Guid versionId, CancellationToken ct) => (await _service.PublishVersionAsync(policyId, versionId, ct)).ToActionResult();

    [HttpPost("resolve"), HasPermission(Permissions.Confirmation.PolicyView)]
    public async Task<ActionResult<ApiResponse<ConfirmationPolicyResolveDto>>> Resolve([FromBody] ConfirmationPolicyResolveRequest request, CancellationToken ct) => (await _service.ResolveAsync(request, ct)).ToActionResult();

    [HttpGet("employees/{employeeId:guid}/status"), HasPermission(Permissions.Confirmation.PolicyView)]
    public async Task<ActionResult<ApiResponse<EmployeeConfirmationStatusDto>>> EmployeeStatus(Guid employeeId, CancellationToken ct) => (await _service.GetEmployeeStatusAsync(employeeId, ct)).ToActionResult();
}
