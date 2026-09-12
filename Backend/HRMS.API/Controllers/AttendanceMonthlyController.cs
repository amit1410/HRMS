using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Authorize, Route("api/attendance")]
public sealed class AttendanceMonthlyController(IAttendanceMonthlyProcessor processor) : ControllerBase
{
    [HttpGet("periods"), HasPermission(Permissions.Attendance.MonthlyViewAll)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendancePeriodDto>>>> Periods([FromQuery] AttendancePeriodQuery query, CancellationToken ct) => (await processor.GetPeriodsAsync(query, ct)).ToActionResult();

    [HttpPost("periods"), HasPermission(Permissions.Attendance.MonthlyProcess)]
    public async Task<ActionResult<ApiResponse<AttendancePeriodDto>>> CreatePeriod(AttendancePeriodRequest request, CancellationToken ct) => (await processor.CreatePeriodAsync(request, ct)).ToCreatedResult(nameof(GetPeriod), x => new { periodId = x.Id });

    [HttpGet("periods/{periodId:guid}"), HasPermission(Permissions.Attendance.MonthlyViewAll)]
    public async Task<ActionResult<ApiResponse<AttendancePeriodDto>>> GetPeriod(Guid periodId, CancellationToken ct) => (await processor.GetPeriodAsync(periodId, ct)).ToActionResult();

    [HttpPost("periods/{periodId:guid}/process"), HasPermission(Permissions.Attendance.MonthlyProcess)]
    public async Task<ActionResult<ApiResponse<AttendancePeriodOverviewDto>>> Process(Guid periodId, CancellationToken ct) => (await processor.ProcessAsync(periodId, ct)).ToActionResult();

    [HttpGet("periods/{periodId:guid}/summary"), HasPermission(Permissions.Attendance.MonthlyViewAll)]
    public async Task<ActionResult<ApiResponse<AttendancePeriodOverviewDto>>> Summary(Guid periodId, CancellationToken ct) => (await processor.GetOverviewAsync(periodId, ct)).ToActionResult();

    [HttpGet("periods/{periodId:guid}/summaries"), HasPermission(Permissions.Attendance.MonthlyViewAll)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeAttendanceMonthlySummaryDto>>>> Summaries(Guid periodId, [FromQuery] AttendanceMonthlySummaryQuery query, CancellationToken ct) => (await processor.GetSummariesAsync(periodId, query, ct)).ToActionResult();

    [HttpGet("periods/{periodId:guid}/exceptions"), HasPermission(Permissions.Attendance.ExceptionView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceExceptionDto>>>> Exceptions(Guid periodId, [FromQuery] AttendanceExceptionQuery query, CancellationToken ct) => (await processor.GetExceptionsAsync(periodId, query, ct)).ToActionResult();
}
