using HRMS.API.Extensions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-calendar"), Produces("application/json")]
[Authorize]
public sealed class LeaveCalendarController : ControllerBase
{
    private readonly ILeaveCalendarService _service;

    public LeaveCalendarController(ILeaveCalendarService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Leave.RequestViewOwn)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveCalendarEventDto>>>> Get(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken = default) =>
        (await _service.GetAsync(from, to, cancellationToken)).ToActionResult();
}
