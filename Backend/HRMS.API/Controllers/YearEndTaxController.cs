using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/year-end-tax")]
public sealed class YearEndTaxController(IYearEndTaxService service) : ControllerBase
{
    [HttpGet("runs"), HasPermission(Permissions.Payroll.YearEndTaxView)] public async Task<ActionResult<ApiResponse<PagedResult<YearEndTaxRunDto>>>> Runs([FromQuery] PagedQuery query, CancellationToken ct) => (await service.GetRunsAsync(query, ct)).ToActionResult();
    [HttpPost("runs"), HasPermission(Permissions.Payroll.YearEndTaxManage)] public async Task<ActionResult<ApiResponse<YearEndTaxRunDto>>> Create(YearEndTaxRunRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(Runs), _ => new { });
    [HttpGet("runs/{id:guid}"), HasPermission(Permissions.Payroll.YearEndTaxView)] public async Task<ActionResult<ApiResponse<YearEndTaxRunDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/calculate"), HasPermission(Permissions.Payroll.YearEndTaxCalculate)] public async Task<ActionResult<ApiResponse<YearEndTaxRunDto>>> Calculate(Guid id, CancellationToken ct) => (await service.CalculateAsync(id, ct)).ToActionResult();
    [HttpGet("runs/{id:guid}/employees"), HasPermission(Permissions.Payroll.YearEndTaxView)] public async Task<ActionResult<ApiResponse<PagedResult<YearEndTaxEmployeeDto>>>> Employees(Guid id, [FromQuery] YearEndTaxEmployeeQuery query, CancellationToken ct) => (await service.GetEmployeesAsync(id, query, ct)).ToActionResult();
    [HttpGet("runs/{id:guid}/employees/{employeeId:guid}"), HasPermission(Permissions.Payroll.YearEndTaxView)] public async Task<ActionResult<ApiResponse<YearEndTaxEmployeeDto>>> Employee(Guid id, Guid employeeId, CancellationToken ct) => (await service.GetEmployeeAsync(id, employeeId, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/previous-employer"), HasPermission(Permissions.Payroll.YearEndTaxManage)] public async Task<ActionResult<ApiResponse<YearEndTaxPreviousEmployerDto>>> AddPreviousEmployer(Guid id, YearEndTaxPreviousEmployerRequest request, CancellationToken ct) => (await service.AddPreviousEmployerAsync(id, request, ct)).ToActionResult();
    [HttpPut("runs/{id:guid}/previous-employer/{inputId:guid}"), HasPermission(Permissions.Payroll.YearEndTaxManage)] public async Task<ActionResult<ApiResponse<YearEndTaxPreviousEmployerDto>>> UpdatePreviousEmployer(Guid id, Guid inputId, YearEndTaxPreviousEmployerUpdateRequest request, CancellationToken ct) => (await service.UpdatePreviousEmployerAsync(id, inputId, request, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/submit"), HasPermission(Permissions.Payroll.YearEndTaxSubmit)] public async Task<ActionResult<ApiResponse<YearEndTaxRunDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/approve"), HasPermission(Permissions.Payroll.YearEndTaxApprove)] public async Task<ActionResult<ApiResponse<YearEndTaxRunDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/close"), HasPermission(Permissions.Payroll.YearEndTaxClose)] public async Task<ActionResult<ApiResponse<YearEndTaxRunDto>>> Close(Guid id, CancellationToken ct) => (await service.CloseAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/cancel"), HasPermission(Permissions.Payroll.YearEndTaxManage)] public async Task<ActionResult<ApiResponse<YearEndTaxRunDto>>> Cancel(Guid id, CancellationToken ct) => (await service.CancelAsync(id, ct)).ToActionResult();
    [HttpPost("runs/{id:guid}/adjustments/handoff"), HasPermission(Permissions.Payroll.YearEndTaxManage)] public async Task<ActionResult<ApiResponse<YearEndTaxAdjustmentDto>>> Handoff(Guid id, YearEndTaxAdjustmentHandoffRequest request, CancellationToken ct) => (await service.HandoffAdjustmentAsync(id, request, ct)).ToActionResult();
    [HttpGet("runs/{id:guid}/history"), HasPermission(Permissions.Payroll.YearEndTaxViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<YearEndTaxHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
    [HttpGet("runs/{id:guid}/export/{kind}"), HasPermission(Permissions.Payroll.YearEndTaxExport)] public async Task<IActionResult> Export(Guid id, string kind, CancellationToken ct) { var result = await service.ExportAsync(id, kind, ct); return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : BadRequest(result.Message); }
}
