using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-periods"), Produces("application/json")]
public sealed class LeavePeriodsController : ControllerBase
{
    private readonly ILeaveConfigurationService _service;
    public LeavePeriodsController(ILeaveConfigurationService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeavePeriodDto>>>> GetAll([FromQuery] LeavePeriodQuery query, CancellationToken ct) => (await _service.GetLeavePeriodsAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeavePeriodDto>>> Get(Guid id, CancellationToken ct) => (await _service.GetLeavePeriodAsync(id, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Leave.PeriodManage)]
    public async Task<ActionResult<ApiResponse<LeavePeriodDto>>> Create([FromBody] LeavePeriodRequest request, CancellationToken ct) => (await _service.CreateLeavePeriodAsync(request, ct)).ToCreatedResult(nameof(Get), x => new { id = x.Id });

    [HttpPut("{id:guid}"), HasPermission(Permissions.Leave.PeriodManage)]
    public async Task<ActionResult<ApiResponse<LeavePeriodDto>>> Update(Guid id, [FromBody] LeavePeriodRequest request, CancellationToken ct) => (await _service.UpdateLeavePeriodAsync(id, request, ct)).ToActionResult();
}
