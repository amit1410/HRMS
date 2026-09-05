using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-approvals"), Produces("application/json")]
[Authorize]
public sealed class LeaveApprovalsController : ControllerBase
{
    private readonly ILeaveApprovalReadService _service;

    public LeaveApprovalsController(ILeaveApprovalReadService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Leave.Approve)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveApprovalListItemDto>>>> GetInbox(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        (await _service.GetInboxAsync(page, pageSize, cancellationToken)).ToActionResult();

    [HttpGet("{requestId:guid}"), HasPermission(Permissions.Leave.Approve)]
    public async Task<ActionResult<ApiResponse<LeaveApprovalDetailDto>>> GetById(
        Guid requestId,
        CancellationToken cancellationToken = default) =>
        (await _service.GetByIdAsync(requestId, cancellationToken)).ToActionResult();
}
