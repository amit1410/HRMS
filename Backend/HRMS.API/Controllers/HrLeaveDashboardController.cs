using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-dashboard"), Produces("application/json"), Authorize]
public sealed class HrLeaveDashboardController : ControllerBase
{
    private readonly IHrLeaveDashboardService _service;
    public HrLeaveDashboardController(IHrLeaveDashboardService service) => _service = service;

    [HttpGet("hr-summary"), HasPermission(Permissions.Leave.DashboardViewAll)]
    public async Task<ActionResult<ApiResponse<HrLeaveDashboardSummaryDto>>> GetSummary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? leaveTypeId = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] Guid? workLocationId = null,
        CancellationToken cancellationToken = default) =>
        (await _service.GetSummaryAsync(new(from, to, leaveTypeId, departmentId, workLocationId), cancellationToken)).ToActionResult();
}
