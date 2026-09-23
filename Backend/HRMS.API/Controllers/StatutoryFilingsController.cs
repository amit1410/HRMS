using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/payroll/statutory-filings")]
public sealed class StatutoryFilingsController(IStatutoryFilingService service) : ControllerBase
{
    [HttpGet("definitions"), HasPermission(Permissions.Payroll.StatutoryFilingView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StatutoryFilingDefinitionDto>>>> Definitions(CancellationToken ct) => (await service.GetDefinitionsAsync(ct)).ToActionResult();
    [HttpPost("definitions"), HasPermission(Permissions.Payroll.StatutoryFilingManage)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingDefinitionDto>>> CreateDefinition(StatutoryFilingDefinitionRequest request, CancellationToken ct) => (await service.CreateDefinitionAsync(request, ct)).ToActionResult();
    [HttpGet("connections"), HasPermission(Permissions.Payroll.StatutoryFilingManageConnections)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StatutoryFilingConnectionProfileDto>>>> Connections(CancellationToken ct) => (await service.GetConnectionProfilesAsync(ct)).ToActionResult();
    [HttpPost("connections"), HasPermission(Permissions.Payroll.StatutoryFilingManageConnections)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingConnectionProfileDto>>> CreateConnection(StatutoryFilingConnectionProfileRequest request, CancellationToken ct) => (await service.CreateConnectionProfileAsync(request, ct)).ToActionResult();
    [HttpPut("connections/{id:guid}"), HasPermission(Permissions.Payroll.StatutoryFilingManageConnections)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingConnectionProfileDto>>> UpdateConnection(Guid id, StatutoryFilingConnectionProfileRequest request, CancellationToken ct) => (await service.UpdateConnectionProfileAsync(id, request, ct)).ToActionResult();
    [HttpPost("connections/{id:guid}/validate"), HasPermission(Permissions.Payroll.StatutoryFilingManageConnections)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingConnectionProfileDto>>> ValidateConnection(Guid id, CancellationToken ct) => (await service.ValidateConnectionProfileAsync(id, ct)).ToActionResult();
    [HttpPost("runs"), HasPermission(Permissions.Payroll.StatutoryFilingManage)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> CreateRun(StatutoryFilingRunRequest request, CancellationToken ct) => (await service.CreateRunAsync(request, ct)).ToActionResult();
    [HttpGet("runs"), HasPermission(Permissions.Payroll.StatutoryFilingView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StatutoryFilingRunDto>>>> Runs(CancellationToken ct) => (await service.GetRunsAsync(ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/generate"), HasPermission(Permissions.Payroll.StatutoryFilingGenerate)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> Generate(Guid id, CancellationToken ct) => (await service.GenerateAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/validate"), HasPermission(Permissions.Payroll.StatutoryFilingValidate)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> Validate(Guid id, CancellationToken ct) => (await service.ValidateAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/submit-for-approval"), HasPermission(Permissions.Payroll.StatutoryFilingSubmitForApproval)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> SubmitForApproval(Guid id, CancellationToken ct) => (await service.SubmitForApprovalAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/approve"), HasPermission(Permissions.Payroll.StatutoryFilingApprove)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/submit"), HasPermission(Permissions.Payroll.StatutoryFilingSubmit)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/reject"), HasPermission(Permissions.Payroll.StatutoryFilingApprove)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> Reject(Guid id, [FromQuery] string reason, CancellationToken ct) => (await service.RejectAsync(id, reason, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/acknowledgements"), HasPermission(Permissions.Payroll.StatutoryFilingSubmit)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> Acknowledge(Guid id, StatutoryFilingAcknowledgementRequest request, CancellationToken ct) => (await service.AcknowledgeAsync(id, request, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/cancel"), HasPermission(Permissions.Payroll.StatutoryFilingCancel)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingRunDto>>> Cancel(Guid id, CancellationToken ct) => (await service.CancelAsync(id, ct)).ToActionResult();
    [HttpGet("runs/{id:guid}/package"), HasPermission(Permissions.Payroll.StatutoryFilingView)]
    public async Task<ActionResult<ApiResponse<StatutoryFilingPackageDto>>> Package(Guid id, CancellationToken ct) => (await service.GetPackageAsync(id, ct)).ToActionResult();
    [HttpGet("runs/{id:guid}/download"), HasPermission(Permissions.Payroll.StatutoryFilingExport)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct) { var result = await service.DownloadAsync(id, ct); if (result.Succeeded) return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName); return result.Status == ResultStatus.NotFound ? NotFound(result.Message) : Conflict(result.Message); }
    [HttpGet("runs/{id:guid}/issues"), HasPermission(Permissions.Payroll.StatutoryFilingView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StatutoryFilingIssueDto>>>> Issues(Guid id, CancellationToken ct) => (await service.GetIssuesAsync(id, ct)).ToActionResult();
}
