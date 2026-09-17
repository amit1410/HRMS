using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/attendance"), Produces("application/json"), Authorize]
public sealed class AttendanceReadController(IAttendanceReadService service) : ControllerBase
{
    [HttpGet("me/calendar")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AttendanceCalendarDayDto>>>> MyCalendar([FromQuery] AttendanceCalendarQuery query, CancellationToken ct) => (await service.GetMyCalendarAsync(query, ct)).ToActionResult();

    [HttpGet("me/days/{date}")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<ApiResponse<AttendanceDayDetailDto>>> MyDay(DateOnly date, CancellationToken ct) => (await service.GetMyDayAsync(date, ct)).ToActionResult();

    [HttpGet("manager/team"), HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<ApiResponse<ManagerAttendanceResult>>> ManagerTeam([FromQuery] ManagerAttendanceQuery query, CancellationToken ct) => (await service.GetManagerTeamAsync(query, ct)).ToActionResult();

    [HttpGet("manager/team/{employeeId:guid}/{date}"), HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<ApiResponse<AttendanceDayDetailDto>>> ManagerDay(Guid employeeId, DateOnly date, CancellationToken ct) => (await service.GetManagerDayAsync(employeeId, date, ct)).ToActionResult();
}
