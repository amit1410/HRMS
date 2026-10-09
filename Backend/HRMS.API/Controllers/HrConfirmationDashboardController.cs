using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/confirmation-dashboard"), Produces("application/json"), Authorize]
public sealed class HrConfirmationDashboardController : ControllerBase
{
    private readonly IHrConfirmationDashboardService _service;
    public HrConfirmationDashboardController(IHrConfirmationDashboardService service) => _service = service;

    [HttpGet("hr-summary"), HasPermission(Permissions.Confirmation.DashboardViewAll)]
    public async Task<ActionResult<ApiResponse<HrConfirmationDashboardSummaryDto>>> GetSummary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] Guid? designationId = null,
        CancellationToken cancellationToken = default) =>
        (await _service.GetSummaryAsync(new(from, to, departmentId, designationId), cancellationToken)).ToActionResult();
}