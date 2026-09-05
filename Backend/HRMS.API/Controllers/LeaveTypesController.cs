using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-types"), Produces("application/json")]
public sealed class LeaveTypesController : ControllerBase
{
    private readonly ILeaveConfigurationService _service;
    public LeaveTypesController(ILeaveConfigurationService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveTypeDto>>>> GetAll([FromQuery] LeaveTypeQuery query, CancellationToken ct) => (await _service.GetLeaveTypesAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<LeaveTypeDto>>> Get(Guid id, CancellationToken ct) => (await _service.GetLeaveTypeAsync(id, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Leave.TypeManage)]
    public async Task<ActionResult<ApiResponse<LeaveTypeDto>>> Create([FromBody] LeaveTypeRequest request, CancellationToken ct) => (await _service.CreateLeaveTypeAsync(request, ct)).ToCreatedResult(nameof(Get), x => new { id = x.Id });

    [HttpPut("{id:guid}"), HasPermission(Permissions.Leave.TypeManage)]
    public async Task<ActionResult<ApiResponse<LeaveTypeDto>>> Update(Guid id, [FromBody] LeaveTypeRequest request, CancellationToken ct) => (await _service.UpdateLeaveTypeAsync(id, request, ct)).ToActionResult();
}
