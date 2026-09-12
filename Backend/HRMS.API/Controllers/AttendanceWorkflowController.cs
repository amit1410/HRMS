using HRMS.API.Extensions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Authorize, Route("api/attendance")]
public sealed class AttendanceWorkflowController(IAttendanceWorkflowService service) : ControllerBase
{
    [HttpPost("me/regularizations"), HasPermission(Permissions.Attendance.RegularizationRequest)] public async Task<ActionResult<ApiResponse<RegularizationDto>>> SubmitRegularization(RegularizationRequest request, CancellationToken ct) => (await service.SubmitRegularizationAsync(new(request.BusinessDate, request.RequestType, request.ProposedInAtUtc, request.ProposedOutAtUtc, request.Reason), ct)).ToActionResult();
    [HttpGet("me/regularizations"), HasPermission(Permissions.Attendance.View)] public async Task<ActionResult<ApiResponse<PagedResult<RegularizationDto>>>> MyRegularizations([FromQuery] PagedQueryModel query, CancellationToken ct) => (await service.GetMyRegularizationsAsync(query, ct)).ToActionResult();
    [HttpGet("me/regularizations/{id:guid}"), HasPermission(Permissions.Attendance.View)] public async Task<ActionResult<ApiResponse<RegularizationDto>>> MyRegularization(Guid id, CancellationToken ct) => (await service.GetMyRegularizationAsync(id, ct)).ToActionResult();
    [HttpPost("me/regularizations/{id:guid}/cancel"), HasPermission(Permissions.Attendance.RegularizationRequest)] public async Task<ActionResult<ApiResponse<RegularizationDto>>> CancelRegularization(Guid id, CancellationToken ct) => (await service.CancelRegularizationAsync(id, ct)).ToActionResult();
    [HttpGet("manager/regularizations"), HasPermission(Permissions.Attendance.RegularizationApprove)] public async Task<ActionResult<ApiResponse<PagedResult<RegularizationDto>>>> ManagerRegularizations([FromQuery] PagedQueryModel query, CancellationToken ct) => (await service.GetManagerRegularizationsAsync(query, ct)).ToActionResult();
    [HttpPost("manager/regularizations/{id:guid}/approve"), HasPermission(Permissions.Attendance.RegularizationApprove)] public async Task<ActionResult<ApiResponse<RegularizationDto>>> ApproveRegularization(Guid id, CancellationToken ct) => (await service.ApproveRegularizationAsync(id, ct)).ToActionResult();
    [HttpPost("manager/regularizations/{id:guid}/reject"), HasPermission(Permissions.Attendance.RegularizationApprove)] public async Task<ActionResult<ApiResponse<RegularizationDto>>> RejectRegularization(Guid id, [FromBody] CommentRequest request, CancellationToken ct) => (await service.RejectRegularizationAsync(id, request.Comments, ct)).ToActionResult();
    [HttpPost("me/on-duty"), HasPermission(Permissions.Attendance.OnDutyRequest)] public async Task<ActionResult<ApiResponse<OnDutyDto>>> SubmitOnDuty(OnDutyRequest request, CancellationToken ct) => (await service.SubmitOnDutyAsync(new(request.StartDate, request.EndDate, request.Reason, request.Purpose, request.Location), ct)).ToActionResult();
    [HttpGet("me/on-duty"), HasPermission(Permissions.Attendance.View)] public async Task<ActionResult<ApiResponse<PagedResult<OnDutyDto>>>> MyOnDuty([FromQuery] PagedQueryModel query, CancellationToken ct) => (await service.GetMyOnDutyAsync(query, ct)).ToActionResult();
    [HttpGet("me/on-duty/{id:guid}"), HasPermission(Permissions.Attendance.View)] public async Task<ActionResult<ApiResponse<OnDutyDto>>> MyOnDutyById(Guid id, CancellationToken ct) => (await service.GetMyOnDutyByIdAsync(id, ct)).ToActionResult();
    [HttpPost("me/on-duty/{id:guid}/cancel"), HasPermission(Permissions.Attendance.OnDutyRequest)] public async Task<ActionResult<ApiResponse<OnDutyDto>>> CancelOnDuty(Guid id, CancellationToken ct) => (await service.CancelOnDutyAsync(id, ct)).ToActionResult();
    [HttpGet("manager/on-duty"), HasPermission(Permissions.Attendance.OnDutyApprove)] public async Task<ActionResult<ApiResponse<PagedResult<OnDutyDto>>>> ManagerOnDuty([FromQuery] PagedQueryModel query, CancellationToken ct) => (await service.GetManagerOnDutyAsync(query, ct)).ToActionResult();
    [HttpPost("manager/on-duty/{id:guid}/approve"), HasPermission(Permissions.Attendance.OnDutyApprove)] public async Task<ActionResult<ApiResponse<OnDutyDto>>> ApproveOnDuty(Guid id, CancellationToken ct) => (await service.ApproveOnDutyAsync(id, ct)).ToActionResult();
    [HttpPost("manager/on-duty/{id:guid}/reject"), HasPermission(Permissions.Attendance.OnDutyApprove)] public async Task<ActionResult<ApiResponse<OnDutyDto>>> RejectOnDuty(Guid id, [FromBody] CommentRequest request, CancellationToken ct) => (await service.RejectOnDutyAsync(id, request.Comments, ct)).ToActionResult();
}

public sealed record RegularizationRequest(DateOnly BusinessDate, HRMS.Domain.Enums.AttendanceRegularizationType RequestType, DateTime? ProposedInAtUtc, DateTime? ProposedOutAtUtc, string Reason);
public sealed record OnDutyRequest(DateOnly StartDate, DateOnly EndDate, string Reason, string? Purpose, string? Location);
public sealed record CommentRequest(string Comments);
public sealed class PagedQueryModel : PagedQuery { }
