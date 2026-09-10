using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-reports"), Produces("application/json"), Authorize]
public sealed class LeaveReportsController : ControllerBase
{
    private readonly ILeaveReportService _reports;
    public LeaveReportsController(ILeaveReportService reports) => _reports = reports;

    [HttpGet("requests"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveRequestReportRow>>>> Requests([FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetRequestsAsync(query, ct)).ToActionResult();

    [HttpGet("employees/{employeeId:guid}/history"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveRequestReportRow>>>> EmployeeHistory(Guid employeeId, [FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetEmployeeHistoryAsync(employeeId, query, ct)).ToActionResult();

    [HttpGet("balances"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveBalanceReportRow>>>> Balances([FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetBalancesAsync(query, ct)).ToActionResult();

    [HttpGet("usage"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveUsageReportRow>>>> Usage([FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetUsageAsync(query, ct)).ToActionResult();

    [HttpGet("accounting"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveAccountingReportRow>>>> Accounting([FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetAccountingAsync(query, ct)).ToActionResult();

    [HttpGet("pending"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<PagedResult<PendingApprovalReportRow>>>> Pending([FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetPendingAsync(query, ct)).ToActionResult();

    [HttpGet("organization"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveOrganizationReportRow>>>> Organization([FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetOrganizationAsync(query, ct)).ToActionResult();

    [HttpGet("calendar"), HasPermission(Permissions.Leave.ReportsView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveCalendarReportRow>>>> Calendar([FromQuery] LeaveReportQuery query, CancellationToken ct) => (await _reports.GetCalendarAsync(query, ct)).ToActionResult();

    [HttpGet("{report}/export.csv"), HasPermission(Permissions.Leave.ReportsExport)]
    public async Task<ActionResult> Export(string report, [FromQuery] LeaveReportQuery query, CancellationToken ct)
    {
        var result = await _reports.ExportCsvAsync(report, query, ct);
        if (result.Succeeded)
            return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
        var error = result.ToActionResult();
        return error.Result ?? new BadRequestObjectResult(error.Value);
    }
}
