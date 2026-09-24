using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/separation")]
public sealed class SeparationController(ISeparationService service) : ControllerBase
{
    [HttpGet("reasons"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationReasonDto>>>> Reasons(CancellationToken ct) => (await service.GetReasonsAsync(ct)).ToActionResult();
    [HttpPost("reasons"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationReasonDto>>> CreateReason(SeparationReasonRequest request, CancellationToken ct) => (await service.CreateReasonAsync(request, ct)).ToActionResult();
    [HttpPut("reasons/{id:guid}"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationReasonDto>>> UpdateReason(Guid id, SeparationReasonRequest request, CancellationToken ct) => (await service.UpdateReasonAsync(id, request, ct)).ToActionResult();
    [HttpPost("me"), HasPermission(Permissions.Separation.CreateSelf)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> CreateSelf(SeparationRequest request, CancellationToken ct) => (await service.CreateSelfAsync(request, ct)).ToCreatedResult(nameof(Get), _ => new { });
    [HttpGet("me/current"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> Current(CancellationToken ct) => (await service.GetCurrentSelfAsync(ct)).ToActionResult();
    [HttpGet("me/history"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeSeparationDto>>>> Mine(CancellationToken ct) => (await service.GetMineAsync(ct)).ToActionResult();
    [HttpPost("employees/{employeeId:guid}"), HasPermission(Permissions.Separation.Initiate)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> CreateForEmployee(Guid employeeId, HrSeparationRequest request, CancellationToken ct) => (await service.CreateForEmployeeAsync(employeeId, request, ct)).ToCreatedResult(nameof(Get), _ => new { });
    [HttpGet, HasPermission(Permissions.Separation.ViewAll)] public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeSeparationDto>>>> All(CancellationToken ct) => (await service.GetAllAsync(ct)).ToActionResult();
    [HttpGet("team"), HasPermission(Permissions.Separation.ViewTeam)] public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeSeparationDto>>>> Team(CancellationToken ct) => (await service.GetTeamAsync(ct)).ToActionResult();
    [HttpGet("inbox/manager"), HasPermission(Permissions.Separation.ManagerReview)] public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeSeparationDto>>>> ManagerInbox(CancellationToken ct) => (await service.GetManagerInboxAsync(ct)).ToActionResult();
    [HttpGet("inbox/hr"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeSeparationDto>>>> HrInbox(CancellationToken ct) => (await service.GetHrInboxAsync(ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Separation.ViewAll)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/history"), HasPermission(Permissions.Separation.ViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationEventDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/submit"), HasPermission(Permissions.Separation.Submit)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/withdraw"), HasPermission(Permissions.Separation.Withdraw)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> Withdraw(Guid id, CancellationToken ct) => (await service.WithdrawAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/manager-approve"), HasPermission(Permissions.Separation.ManagerReview)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> ManagerApprove(Guid id, CancellationToken ct) => (await service.ManagerApproveAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/manager-reject"), HasPermission(Permissions.Separation.ManagerReview)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> ManagerReject(Guid id, [FromBody] CommentRequest request, CancellationToken ct) => (await service.ManagerRejectAsync(id, request.Comments, ct)).ToActionResult();
    [HttpPost("{id:guid}/hr-approve"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> HrApprove(Guid id, CancellationToken ct) => (await service.HrApproveAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/hr-reject"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> HrReject(Guid id, [FromBody] CommentRequest request, CancellationToken ct) => (await service.HrRejectAsync(id, request.Comments, ct)).ToActionResult();
    [HttpPost("{id:guid}/revise-lwd"), HasPermission(Permissions.Separation.ReviseLastWorkingDate)] public async Task<ActionResult<ApiResponse<EmployeeSeparationDto>>> ReviseLwd(Guid id, SeparationLwdRevisionRequest request, CancellationToken ct) => (await service.ReviseLwdAsync(id, request, ct)).ToActionResult();
    [HttpGet("{id:guid}/notice"), HasPermission(Permissions.Separation.ViewHistory)] public async Task<ActionResult<ApiResponse<SeparationNoticeDto>>> Notice(Guid id, CancellationToken ct) => (await service.GetNoticeAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/notice/history"), HasPermission(Permissions.Separation.ViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationEventDto>>>> NoticeHistory(Guid id, CancellationToken ct) => (await service.GetNoticeHistoryAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/notice/waive"), HasPermission(Permissions.Separation.NoticeWaive)] public async Task<ActionResult<ApiResponse<SeparationNoticeDto>>> WaiveNotice(Guid id, NoticeWaiverRequest request, CancellationToken ct) => (await service.ApplyNoticeWaiverAsync(id, request, ct)).ToActionResult();
    [HttpPost("{id:guid}/revise-approved-lwd"), HasPermission(Permissions.Separation.ReviseApprovedLastWorkingDate)] public async Task<ActionResult<ApiResponse<SeparationNoticeDto>>> ReviseApprovedLwd(Guid id, SeparationLwdRevisionRequest request, CancellationToken ct) => (await service.ReviseApprovedLwdAsync(id, request, ct)).ToActionResult();
}
