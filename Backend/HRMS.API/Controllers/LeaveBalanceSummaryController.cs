using HRMS.API.Extensions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-balances"), Produces("application/json")]
[Authorize]
public sealed class LeaveBalanceSummaryController : ControllerBase
{
    private readonly ILeaveBalanceSummaryReader _reader;

    public LeaveBalanceSummaryController(ILeaveBalanceSummaryReader reader) => _reader = reader;

    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveBalanceSummaryDto>>>> GetMine(CancellationToken cancellationToken = default) =>
        (await _reader.GetMineAsync(cancellationToken)).ToActionResult();
}
