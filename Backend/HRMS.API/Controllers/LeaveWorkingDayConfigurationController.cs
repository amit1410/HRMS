using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Produces("application/json")]
public sealed class LeaveWorkingDayConfigurationController(ILeaveWorkingDayConfigurationService service) : ControllerBase
{
    [HttpGet("api/holidays"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<HolidayDto>>>> GetHolidays([FromQuery] HolidayQuery query, CancellationToken ct) => (await service.GetHolidaysAsync(query, ct)).ToActionResult();

    [HttpPost("api/holidays"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<HolidayDto>>> CreateHoliday([FromBody] HolidayRequest request, CancellationToken ct) => (await service.CreateHolidayAsync(request, ct)).ToCreatedResult(nameof(GetHolidays), _ => new { });

    [HttpPut("api/holidays/{id:guid}"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<HolidayDto>>> UpdateHoliday(Guid id, [FromBody] HolidayRequest request, CancellationToken ct) => (await service.UpdateHolidayAsync(id, request, ct)).ToActionResult();

    [HttpDelete("api/holidays/{id:guid}"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<HolidayDto>>> DeactivateHoliday(Guid id, CancellationToken ct) => (await service.DeactivateHolidayAsync(id, ct)).ToActionResult();

    [HttpGet("api/weekly-off-configurations"), HasPermission(Permissions.Leave.PolicyView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WeeklyOffConfigurationDto>>>> GetWeeklyOffs(CancellationToken ct) => (await service.GetWeeklyOffConfigurationsAsync(ct)).ToActionResult();

    [HttpPost("api/weekly-off-configurations"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<WeeklyOffConfigurationDto>>> CreateWeeklyOff([FromBody] WeeklyOffConfigurationRequest request, CancellationToken ct) => (await service.CreateWeeklyOffConfigurationAsync(request, ct)).ToCreatedResult(nameof(GetWeeklyOffs), _ => new { });

    [HttpPut("api/weekly-off-configurations/{id:guid}"), HasPermission(Permissions.Leave.PolicyManage)]
    public async Task<ActionResult<ApiResponse<WeeklyOffConfigurationDto>>> UpdateWeeklyOff(Guid id, [FromBody] WeeklyOffConfigurationRequest request, CancellationToken ct) => (await service.UpdateWeeklyOffConfigurationAsync(id, request, ct)).ToActionResult();
}
