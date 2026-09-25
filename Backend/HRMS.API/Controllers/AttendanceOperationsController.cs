using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Authorize, Route("api/attendance/operations")]
public sealed class AttendanceOperationsController(IAttendanceOperationsService service) : ControllerBase
{
    [HttpGet("exceptions"), HasPermission(Permissions.Attendance.ExceptionView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>> Exceptions([FromQuery] AttendanceOperationsExceptionQuery query, CancellationToken ct) => (await service.GetOperationalExceptionsAsync(query, ct)).ToActionResult();

    [HttpGet("dashboard"), HasPermission(Permissions.Attendance.ExceptionView)]
    public async Task<ActionResult<ApiResponse<AttendanceOperationsDashboardDto>>> Dashboard([FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct) => (await service.GetDashboardAsync(fromDate, toDate, ct)).ToActionResult();

    [HttpPost("bulk"), HasPermission(Permissions.Attendance.RegularizationApprove)]
    public async Task<ActionResult<ApiResponse<AttendanceBulkActionResponse>>> Bulk([FromBody] IReadOnlyList<AttendanceBulkActionItem> items, CancellationToken ct) => (await service.ApplyBulkActionAsync(items, ct)).ToActionResult();

    [HttpGet("exceptions/export"), HasPermission(Permissions.Attendance.ExceptionView), HasPermission(Permissions.Attendance.ReportExport)]
    public async Task<IActionResult> Export([FromQuery] AttendanceOperationsExceptionQuery query, CancellationToken ct)
    {
        var result = await service.ExportExceptionsAsync(query, ct);
        return result.Succeeded ? new FileContentResult(result.Value!.Content, result.Value.ContentType) { FileDownloadName = result.Value.FileName } : result.ToErrorResult();
    }

    [HttpPost("manual"), HasPermission(Permissions.Attendance.AdminCorrectionManage)]
    public async Task<ActionResult<ApiResponse<RegularizationDto>>> Manual([FromBody] ManualAttendanceRequest request, CancellationToken ct) => (await service.SubmitManualAttendanceAsync(request, ct)).ToActionResult();

    [HttpPost("bulk-corrections"), HasPermission(Permissions.Attendance.AdminCorrectionManage)]
    public async Task<ActionResult<ApiResponse<AttendanceBulkCorrectionResponse>>> BulkCorrections([FromBody] IReadOnlyList<AttendanceBulkCorrectionItem> items, CancellationToken ct) => (await service.ApplyBulkCorrectionsAsync(items, ct)).ToActionResult();

    [HttpPost("exceptions/resolve"), HasPermission(Permissions.Attendance.ExceptionView)]
    public async Task<ActionResult<ApiResponse<HRMS.Application.DTOs.Employees.EmployeeAuditLogDto>>> ResolveException([FromBody] AttendanceExceptionResolutionRequest request, CancellationToken ct) => (await service.ResolveExceptionAsync(request, ct)).ToActionResult();

    [HttpGet("employees/{employeeId:guid}/history"), HasPermission(Permissions.Attendance.ExceptionView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>> History(Guid employeeId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => (await service.GetHistoryAsync(employeeId, fromDate, toDate, new AttendanceOperationsHistoryQuery { Page = page, PageSize = pageSize }, ct)).ToActionResult();
}

[ApiController, Authorize, Route("api/attendance/me")]
public sealed class MyAttendanceOperationsController(IAttendanceOperationsService service) : ControllerBase
{
    [HttpGet("exceptions"), HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>> Exceptions([FromQuery] AttendanceOperationsExceptionQuery query, CancellationToken ct) => (await service.GetMyExceptionsAsync(query, ct)).ToActionResult();
}
