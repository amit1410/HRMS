using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/separation/documents")]
public sealed class SeparationDocumentController(ISeparationDocumentService service) : ControllerBase
{
    [HttpGet("templates"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationDocumentTemplateDto>>>> Templates(CancellationToken ct) => (await service.GetTemplatesAsync(ct)).ToActionResult();
    [HttpPost("templates"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationDocumentTemplateDto>>> CreateTemplate(SeparationDocumentTemplateRequest request, CancellationToken ct) => (await service.CreateTemplateAsync(request, ct)).ToActionResult();
    [HttpPost("templates/{templateId:guid}/versions"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationDocumentTemplateDto>>> AddVersion(Guid templateId, SeparationDocumentTemplateVersionRequest request, CancellationToken ct) => (await service.AddVersionAsync(templateId, request, ct)).ToActionResult();
    [HttpPost("templates/{templateId:guid}/versions/{versionId:guid}/publish"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationDocumentTemplateDto>>> Publish(Guid templateId, Guid versionId, [FromQuery] int expectedConcurrencyVersion, CancellationToken ct) => (await service.PublishVersionAsync(templateId, versionId, expectedConcurrencyVersion, ct)).ToActionResult();
    [HttpPost("templates/versions/{versionId:guid}/preview"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationDocumentPreviewDto>>> Preview(Guid versionId, SeparationDocumentPreviewRequest request, CancellationToken ct) => (await service.PreviewAsync(versionId, request, ct)).ToActionResult();
    [HttpGet("for/{separationId:guid}/readiness"), HasPermission(Permissions.Separation.ViewAll)] public async Task<ActionResult<ApiResponse<SeparationDocumentReadinessDto>>> Readiness(Guid separationId, CancellationToken ct) => (await service.GetReadinessAsync(separationId, ct)).ToActionResult();
    [HttpGet("for/{separationId:guid}"), HasPermission(Permissions.Separation.ViewAll)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationGeneratedDocumentDto>>>> ForSeparation(Guid separationId, CancellationToken ct) => (await service.GetForSeparationAsync(separationId, false, ct)).ToActionResult();
    [HttpGet("mine/{separationId:guid}"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationGeneratedDocumentDto>>>> Mine(Guid separationId, CancellationToken ct) => (await service.GetForSeparationAsync(separationId, true, ct)).ToActionResult();
    [HttpPost("for/{separationId:guid}/generate"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationGeneratedDocumentDto>>> Generate(Guid separationId, SeparationDocumentGenerateRequest request, CancellationToken ct) => (await service.GenerateAsync(separationId, request, ct)).ToActionResult();
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationGeneratedDocumentDto>>> Approve(Guid id, SeparationDocumentApproveRequest request, CancellationToken ct) => (await service.ApproveAsync(id, request, ct)).ToActionResult();
    [HttpPost("{id:guid}/issue"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationGeneratedDocumentDto>>> Issue(Guid id, [FromQuery] int expectedConcurrencyVersion, CancellationToken ct) => (await service.IssueAsync(id, expectedConcurrencyVersion, ct)).ToActionResult();
    [HttpPost("{id:guid}/supersede"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationGeneratedDocumentDto>>> Supersede(Guid id, SeparationDocumentSupersedeRequest request, CancellationToken ct) => (await service.SupersedeAsync(id, request, ct)).ToActionResult();
    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.Separation.Manage)] public async Task<ActionResult<ApiResponse<SeparationGeneratedDocumentDto>>> Cancel(Guid id, SeparationDocumentCancelRequest request, CancellationToken ct) => (await service.CancelAsync(id, request, ct)).ToActionResult();
    [HttpGet("{id:guid}/history"), HasPermission(Permissions.Separation.ViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationDocumentEventDto>>>> History(Guid id, CancellationToken ct) => (await service.HistoryAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/download"), HasPermission(Permissions.Separation.ViewAll)] public async Task<IActionResult> Download(Guid id, CancellationToken ct) { var result = await service.DownloadAsync(id, false, ct); return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : StatusCode(result.Status == ResultStatus.NotFound ? 404 : 403, result.Message); }
    [HttpGet("mine/{id:guid}/download"), HasPermission(Permissions.Separation.ViewSelf)] public async Task<IActionResult> DownloadMine(Guid id, CancellationToken ct) { var result = await service.DownloadAsync(id, true, ct); return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : StatusCode(result.Status == ResultStatus.NotFound ? 404 : 403, result.Message); }
    [HttpGet("dashboard"), HasPermission(Permissions.Separation.ViewAll)] public async Task<ActionResult<ApiResponse<PagedResult<SeparationDocumentDashboardItemDto>>>> Dashboard([FromQuery] SeparationDocumentDashboardQuery query, CancellationToken ct) => (await service.DashboardAsync(query, ct)).ToActionResult();
}
