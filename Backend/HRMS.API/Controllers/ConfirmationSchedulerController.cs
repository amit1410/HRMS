using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Phase 9b: tenant configuration for the confirmation/probation reminder scheduler and read access to its
/// execution log. The scheduler itself runs as a background worker; these endpoints only configure and
/// observe it — they never change an employee's confirmation outcome.
/// </summary>
[ApiController, Route("api/confirmation-scheduler"), Produces("application/json")]
public sealed class ConfirmationSchedulerController : ControllerBase
{
    private readonly IConfirmationSchedulerSettingService _service;
    public ConfirmationSchedulerController(IConfirmationSchedulerSettingService service) => _service = service;

    [HttpGet("settings"), HasPermission(Permissions.Confirmation.SchedulerView)]
    public async Task<ActionResult<ApiResponse<ConfirmationSchedulerSettingDto>>> GetSettings(CancellationToken ct) => (await _service.GetAsync(ct)).ToActionResult();

    [HttpPut("settings"), HasPermission(Permissions.Confirmation.SchedulerManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationSchedulerSettingDto>>> UpdateSettings([FromBody] ConfirmationSchedulerSettingRequest request, CancellationToken ct) => (await _service.UpdateAsync(request, ct)).ToActionResult();

    [HttpGet("executions"), HasPermission(Permissions.Confirmation.SchedulerView)]
    public async Task<ActionResult<ApiResponse<PagedResult<ConfirmationReminderDeliveryDto>>>> GetExecutions([FromQuery] ConfirmationReminderLogQuery query, CancellationToken ct) => (await _service.GetExecutionLogAsync(query, ct)).ToActionResult();
}
