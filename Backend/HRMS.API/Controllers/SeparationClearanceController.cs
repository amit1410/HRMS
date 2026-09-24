using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/separation/clearance")]
public sealed class SeparationClearanceController(IClearanceService service) : ControllerBase
{
    [HttpGet("templates"), HasPermission(Permissions.Separation.ClearanceConfigure)] public async Task<ActionResult<ApiResponse<IReadOnlyList<ClearanceTemplateDto>>>> Templates(CancellationToken ct) => (await service.GetTemplatesAsync(ct)).ToActionResult();
    [HttpPost("templates"), HasPermission(Permissions.Separation.ClearanceConfigure)] public async Task<ActionResult<ApiResponse<ClearanceTemplateDto>>> CreateTemplate(ClearanceTemplateRequest request, CancellationToken ct) => (await service.CreateTemplateAsync(request, ct)).ToCreatedResult(nameof(Get), _ => new { });
    [HttpPost("for/{separationId:guid}/start"), HasPermission(Permissions.Separation.ClearanceStart)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> Start(Guid separationId, CancellationToken ct) => (await service.StartAsync(separationId, ct)).ToActionResult();
    [HttpGet("for/{separationId:guid}"), HasPermission(Permissions.Separation.ClearanceViewAll)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> ForSeparation(Guid separationId, CancellationToken ct) => (await service.GetForSeparationAsync(separationId, ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Separation.ClearanceViewAll)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToActionResult();
    [HttpPost("tasks/{taskId:guid}/clear"), HasPermission(Permissions.Separation.ClearanceManageTask)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> Clear(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct) => (await service.ClearTaskAsync(taskId, request, ct)).ToActionResult();
    [HttpPost("tasks/{taskId:guid}/block"), HasPermission(Permissions.Separation.ClearanceManageTask)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> Block(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct) => (await service.BlockTaskAsync(taskId, request, ct)).ToActionResult();
    [HttpPost("tasks/{taskId:guid}/waive"), HasPermission(Permissions.Separation.ClearanceWaive)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> Waive(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct) => (await service.WaiveTaskAsync(taskId, request, ct)).ToActionResult();
    [HttpPost("tasks/{taskId:guid}/assets"), HasPermission(Permissions.Separation.ClearanceAssetManage)] public async Task<ActionResult<ApiResponse<SeparationClearanceTaskDto>>> Asset(Guid taskId, SeparationAssetReturnRequest request, CancellationToken ct) => (await service.AddAssetReturnAsync(taskId, request, ct)).ToActionResult();
    [HttpPost("tasks/{taskId:guid}/assets/{assetId:guid}"), HasPermission(Permissions.Separation.ClearanceAssetManage)] public async Task<ActionResult<ApiResponse<SeparationClearanceTaskDto>>> UpdateAsset(Guid taskId, Guid assetId, SeparationAssetReturnRequest request, CancellationToken ct) => (await service.UpdateAssetReturnAsync(taskId, assetId, request, ct)).ToActionResult();
    [HttpGet("inbox/manager"), HasPermission(Permissions.Separation.ClearanceViewTeam)] public async Task<ActionResult<ApiResponse<PagedResult<ClearanceInboxItemDto>>>> ManagerInbox([FromQuery] ClearanceInboxQuery query, CancellationToken ct) => (await service.GetManagerInboxAsync(query, ct)).ToActionResult();
    [HttpGet("inbox"), HasPermission(Permissions.Separation.ClearanceViewAll)] public async Task<ActionResult<ApiResponse<PagedResult<ClearanceInboxItemDto>>>> FunctionalInbox([FromQuery] ClearanceInboxQuery query, CancellationToken ct) => (await service.GetFunctionalInboxAsync(query, ct)).ToActionResult();
    [HttpGet("hr-dashboard"), HasPermission(Permissions.Separation.ClearanceViewAll)] public async Task<ActionResult<ApiResponse<PagedResult<ClearanceDashboardItemDto>>>> HrDashboard([FromQuery] ClearanceInboxQuery query, CancellationToken ct) => (await service.GetHrDashboardAsync(query, ct)).ToActionResult();
    [HttpPost("{id:guid}/complete"), HasPermission(Permissions.Separation.ClearanceComplete)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> Complete(Guid id, CancellationToken ct) => (await service.CompleteAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/reopen"), HasPermission(Permissions.Separation.ClearanceReopen)] public async Task<ActionResult<ApiResponse<SeparationClearanceDto>>> Reopen(Guid id, [FromBody] CommentRequest request, CancellationToken ct) => (await service.ReopenAsync(id, request.Comments, ct)).ToActionResult();
}
