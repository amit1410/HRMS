using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Authorize, Route("api/attendance/reports")]
public sealed class AttendanceReportController(IAttendanceReportService reports) : ControllerBase
{
    [HttpGet("daily"), HasPermission(Permissions.Attendance.ReportView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceDailyReportRow>>>> Daily([FromQuery] AttendanceDailyReportQuery query, CancellationToken ct) => (await reports.GetDailyAsync(query, ct)).ToActionResult();

    [HttpGet("monthly"), HasPermission(Permissions.Attendance.ReportView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceMonthlyReportRow>>>> Monthly([FromQuery] AttendanceMonthlyReportQuery query, CancellationToken ct) => (await reports.GetMonthlyAsync(query, ct)).ToActionResult();

    [HttpGet("exceptions"), HasPermission(Permissions.Attendance.ReportView), HasPermission(Permissions.Attendance.ExceptionView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceExceptionDto>>>> Exceptions([FromQuery] AttendanceExceptionReportQuery query, CancellationToken ct) => (await reports.GetExceptionsAsync(query, ct)).ToActionResult();

    [HttpGet("daily/export"), HasPermission(Permissions.Attendance.ReportView), HasPermission(Permissions.Attendance.ReportExport)]
    public async Task<IActionResult> DailyExport([FromQuery] AttendanceDailyReportQuery query, CancellationToken ct) => await Export(() => reports.ExportDailyAsync(query, ct));

    [HttpGet("monthly/export"), HasPermission(Permissions.Attendance.ReportView), HasPermission(Permissions.Attendance.ReportExport)]
    public async Task<IActionResult> MonthlyExport([FromQuery] AttendanceMonthlyReportQuery query, CancellationToken ct) => await Export(() => reports.ExportMonthlyAsync(query, ct));

    [HttpGet("exceptions/export"), HasPermission(Permissions.Attendance.ReportView), HasPermission(Permissions.Attendance.ReportExport), HasPermission(Permissions.Attendance.ExceptionView)]
    public async Task<IActionResult> ExceptionsExport([FromQuery] AttendanceExceptionReportQuery query, CancellationToken ct) => await Export(() => reports.ExportExceptionsAsync(query, ct));

    private static async Task<IActionResult> Export(Func<Task<Result<AttendanceReportExport>>> action)
    {
        var result = await action();
        return result.Succeeded ? new FileContentResult(result.Value!.Content, result.Value.ContentType) { FileDownloadName = result.Value.FileName } : result.ToErrorResult();
    }
}
