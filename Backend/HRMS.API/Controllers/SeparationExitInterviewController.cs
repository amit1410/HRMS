using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/separation/exit-interview")]
public sealed class SeparationExitInterviewController(IExitInterviewService service) : ControllerBase
{
    [HttpGet("templates"), HasPermission(Permissions.Separation.ClearanceConfigure)] public async Task<ActionResult<ApiResponse<IReadOnlyList<ExitInterviewTemplateDto>>>> Templates(CancellationToken ct) => (await service.GetTemplatesAsync(ct)).ToActionResult();
    [HttpPost("templates"), HasPermission(Permissions.Separation.ClearanceConfigure)] public async Task<ActionResult<ApiResponse<ExitInterviewTemplateDto>>> CreateTemplate(ExitInterviewTemplateRequest request, CancellationToken ct) => (await service.CreateTemplateAsync(request, ct)).ToActionResult();
    [HttpPost("templates/{templateId:guid}/versions"), HasPermission(Permissions.Separation.ClearanceConfigure)] public async Task<ActionResult<ApiResponse<ExitInterviewTemplateVersionDto>>> CreateVersion(Guid templateId, ExitInterviewVersionRequest request, CancellationToken ct) => (await service.CreateVersionAsync(templateId, request, ct)).ToActionResult();
    [HttpPost("template-versions/{versionId:guid}/publish"), HasPermission(Permissions.Separation.ClearanceConfigure)] public async Task<ActionResult<ApiResponse<ExitInterviewTemplateVersionDto>>> Publish(Guid versionId, CancellationToken ct) => (await service.PublishVersionAsync(versionId, ct)).ToActionResult();
    [HttpPost("for/{separationId:guid}/assign"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<ExitInterviewHrDto>>> Assign(Guid separationId, ExitInterviewAssignRequest request, CancellationToken ct) => (await service.AssignAsync(separationId, request, ct)).ToActionResult();
    [HttpGet("me"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<ExitInterviewEmployeeDto>>> Mine(CancellationToken ct) => (await service.GetMineAsync(null, ct)).ToActionResult();
    [HttpGet("me/{separationId:guid}"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<ExitInterviewEmployeeDto>>> MineForSeparation(Guid separationId, CancellationToken ct) => (await service.GetMineAsync(separationId, ct)).ToActionResult();
    [HttpPut("for/{separationId:guid}/my-responses"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<ExitInterviewEmployeeDto>>> Save(Guid separationId, ExitInterviewDraftRequest request, CancellationToken ct) => (await service.SaveDraftAsync(separationId, request, ct)).ToActionResult();
    [HttpPost("for/{separationId:guid}/submit"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<ExitInterviewEmployeeDto>>> Submit(Guid separationId, CancellationToken ct) => (await service.SubmitAsync(separationId, ct)).ToActionResult();
    [HttpGet("inbox"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<PagedResult<ExitInterviewInboxItemDto>>>> Inbox([FromQuery] ExitInterviewInboxQuery query, CancellationToken ct) => (await service.GetInboxAsync(query, ct)).ToActionResult();
    [HttpGet("for/{separationId:guid}"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<ExitInterviewHrDto>>> Get(Guid separationId, CancellationToken ct) => (await service.GetHrAsync(separationId, ct)).ToActionResult();
    [HttpPost("for/{separationId:guid}/start-hr"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<ExitInterviewHrDto>>> StartHr(Guid separationId, CancellationToken ct) => (await service.StartHrAsync(separationId, ct)).ToActionResult();
    [HttpPost("for/{separationId:guid}/hr-note"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<ExitInterviewHrDto>>> Note(Guid separationId, ExitInterviewNoteRequest request, CancellationToken ct) => (await service.AddHrNoteAsync(separationId, request, ct)).ToActionResult();
    [HttpPost("for/{separationId:guid}/complete"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<ExitInterviewHrDto>>> Complete(Guid separationId, ExitInterviewCompletionRequest request, CancellationToken ct) => (await service.CompleteAsync(separationId, request, ct)).ToActionResult();
    [HttpPost("for/{separationId:guid}/reopen"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<ExitInterviewHrDto>>> Reopen(Guid separationId, ExitInterviewReopenRequest request, CancellationToken ct) => (await service.ReopenAsync(separationId, request, ct)).ToActionResult();
    [HttpGet("analytics"), HasPermission(Permissions.Separation.HrReview)] public async Task<ActionResult<ApiResponse<ExitInterviewAnalyticsDto>>> Analytics(CancellationToken ct) => (await service.GetAnalyticsAsync(ct)).ToActionResult();
}
