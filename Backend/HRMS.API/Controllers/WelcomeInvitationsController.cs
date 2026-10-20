using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Communication;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/communication/welcome-invitations")]
[Produces("application/json")]
public sealed class WelcomeInvitationsController : ControllerBase
{
    private readonly IWelcomeInvitationService _service;
    public WelcomeInvitationsController(IWelcomeInvitationService service) => _service = service;

    [HttpGet]
    [HasPermission(Permissions.WelcomeInvitation.View)]
    public async Task<ActionResult<ApiResponse<WelcomeInvitationList>>> List([FromQuery] WelcomeInvitationQuery query, CancellationToken ct) => (await _service.ListAsync(query, ct)).ToActionResult();

    [HttpPost("preview")]
    [HasPermission(Permissions.WelcomeInvitation.Send)]
    public async Task<ActionResult<ApiResponse<WelcomeInvitationPreview>>> Preview(WelcomeInvitationSelectionRequest request, CancellationToken ct) => (await _service.PreviewAsync(request, ct)).ToActionResult();

    [HttpPost("send")]
    [HasPermission(Permissions.WelcomeInvitation.Send)]
    public async Task<ActionResult<ApiResponse<WelcomeInvitationSendResult>>> Send(WelcomeInvitationSelectionRequest request, CancellationToken ct) => (await _service.SendAsync(request, ct)).ToActionResult();

    [HttpPost("{employeeId:guid}/resend")]
    [HasPermission(Permissions.WelcomeInvitation.Resend)]
    public async Task<ActionResult<ApiResponse<WelcomeInvitationSendResult>>> Resend(Guid employeeId, CancellationToken ct) => (await _service.SendAsync(new WelcomeInvitationSelectionRequest { EmployeeIds = [employeeId], Resend = true, IdempotencyKey = Guid.NewGuid().ToString("N") }, ct)).ToActionResult();

    [HttpPost("{employeeId:guid}/retry")]
    [HasPermission(Permissions.WelcomeInvitation.Retry)]
    public async Task<ActionResult<ApiResponse<WelcomeInvitationSendResult>>> Retry(Guid employeeId, CancellationToken ct) => (await _service.RetryAsync(employeeId, ct)).ToActionResult();

    [HttpGet("batches/{batchId:guid}")]
    [HasPermission(Permissions.WelcomeInvitation.View)]
    public async Task<ActionResult<ApiResponse<WelcomeInvitationBatchStatusDto>>> BatchStatus(Guid batchId, CancellationToken ct) => (await _service.GetBatchStatusAsync(batchId, ct)).ToActionResult();

    [HttpGet("{employeeId:guid}/history")]
    [HasPermission(Permissions.WelcomeInvitation.ViewHistory)]
    public async Task<ActionResult<ApiResponse<PagedResult<WelcomeInvitationHistoryRow>>>> History(Guid employeeId, [FromQuery] PagedQuery query, CancellationToken ct) => (await _service.HistoryAsync(employeeId, query, ct)).ToActionResult();
}
