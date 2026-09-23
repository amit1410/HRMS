using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/input-batches")]
public sealed class PayrollInputsController(IPayrollInputService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.InputView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollInputBatchDto>>>> Get([FromQuery] PayrollInputBatchQuery query, CancellationToken ct) => (await service.GetBatchesAsync(query, ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.InputCreate)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Create(PayrollInputBatchRequest request, CancellationToken ct) => (await service.CreateBatchAsync(request, ct)).ToCreatedResult(nameof(Get), x => new { id = x.Id });
    [HttpPost("{id:guid}/upload"), HasPermission(Permissions.Payroll.InputImport)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Upload(Guid id, IFormFile file, CancellationToken ct) { if (file is null) return BadRequest(ApiResponse.Fail("A CSV file is required.")); await using var stream = file.OpenReadStream(); return (await service.UploadAsync(id, stream, file.FileName, ct)).ToActionResult(); }
    [HttpPost("{id:guid}/validate"), HasPermission(Permissions.Payroll.InputValidate)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Validate(Guid id, CancellationToken ct) => (await service.ValidateAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/preview"), HasPermission(Permissions.Payroll.InputView)] public async Task<ActionResult<ApiResponse<PayrollInputPreviewDto>>> Preview(Guid id, CancellationToken ct) => (await service.GetPreviewAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/preview/export"), HasPermission(Permissions.Payroll.InputView)] public async Task<IActionResult> PreviewExport(Guid id, CancellationToken ct) => await ExportAsync(() => service.ExportPreviewAsync(id, ct));
    [HttpGet("{id:guid}/lines"), HasPermission(Permissions.Payroll.InputView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollInputLineDto>>>> Lines(Guid id, int page = 1, int pageSize = 50, PayrollInputLineStatus? status = null, CancellationToken ct = default) => (await service.GetLinesAsync(id, page, pageSize, status, ct)).ToActionResult();
    [HttpPut("{id:guid}/lines/{lineId:guid}"), HasPermission(Permissions.Payroll.InputCreate)] public async Task<ActionResult<ApiResponse<PayrollInputLineDto>>> UpdateLine(Guid id, Guid lineId, PayrollInputLineUpdateRequest request, CancellationToken ct) => (await service.UpdateLineAsync(id, lineId, request, ct)).ToActionResult();
    [HttpDelete("{id:guid}/lines/{lineId:guid}"), HasPermission(Permissions.Payroll.InputCreate)] public async Task<ActionResult<ApiResponse<bool>>> DeleteLine(Guid id, Guid lineId, CancellationToken ct) => (await service.DeleteLineAsync(id, lineId, ct)).ToActionResult();
    [HttpGet("{id:guid}/issues"), HasPermission(Permissions.Payroll.InputView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollInputIssueDto>>>> Issues(Guid id, int page = 1, int pageSize = 50, PayrollInputIssueSeverity? severity = null, CancellationToken ct = default) => (await service.GetIssuesAsync(id, page, pageSize, severity, ct)).ToActionResult();
    [HttpGet("{id:guid}/issues/export"), HasPermission(Permissions.Payroll.InputView)] public async Task<IActionResult> IssuesExport(Guid id, CancellationToken ct) => await ExportAsync(() => service.ExportIssuesAsync(id, ct));
    [HttpPost("{id:guid}/submit"), HasPermission(Permissions.Payroll.InputSubmit)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Payroll.InputApprove)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/reject"), HasPermission(Permissions.Payroll.InputApprove)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Reject(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.RejectAsync(id, reason, ct)).ToActionResult();
    [HttpPost("{id:guid}/post"), HasPermission(Permissions.Payroll.InputPost)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Post(Guid id, CancellationToken ct) => (await service.PostAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/result/export"), HasPermission(Permissions.Payroll.InputView)] public async Task<IActionResult> ResultExport(Guid id, CancellationToken ct) => await ExportAsync(() => service.ExportResultAsync(id, ct));
    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.Payroll.InputCancel)] public async Task<ActionResult<ApiResponse<PayrollInputBatchDto>>> Cancel(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.CancelAsync(id, reason, ct)).ToActionResult();
    [HttpGet("{id:guid}/history"), HasPermission(Permissions.Payroll.InputViewAudit)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollInputHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();

    private static async Task<IActionResult> ExportAsync(Func<Task<Result<PayrollOutputFile>>> operation)
    {
        var result = await operation();
        return result.Succeeded ? new FileContentResult(result.Value!.Content, result.Value.ContentType) { FileDownloadName = result.Value.FileName } : new BadRequestObjectResult(ApiResponse.Fail(result.Message ?? "Export failed."));
    }
}

[ApiController, Route("api/payroll/input-templates")]
public sealed class PayrollInputTemplatesController(IPayrollInputService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.InputView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollInputTemplateDto>>>> Get(CancellationToken ct) => (await service.GetTemplatesAsync(ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.InputConfigureTemplate)] public async Task<ActionResult<ApiResponse<PayrollInputTemplateDto>>> Create(PayrollInputTemplateRequest request, CancellationToken ct) => (await service.CreateTemplateAsync(request, ct)).ToCreatedResult(nameof(Get), _ => new { });
    [HttpPut("{id:guid}"), HasPermission(Permissions.Payroll.InputConfigureTemplate)] public async Task<ActionResult<ApiResponse<PayrollInputTemplateDto>>> Update(Guid id, PayrollInputTemplateRequest request, CancellationToken ct) => (await service.UpdateTemplateAsync(id, request, ct)).ToActionResult();
}
