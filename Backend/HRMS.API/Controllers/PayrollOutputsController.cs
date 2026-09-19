using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll")]
public sealed class PayrollOutputsController(IPayrollOutputService service) : ControllerBase
{
    [HttpPost("runs/{runId:guid}/payslips/generate"), HasPermission(Permissions.Payroll.PayslipGenerate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipDto>>>> Generate(Guid runId, CancellationToken ct) => (await service.GenerateAsync(runId, ct)).ToActionResult();

    [HttpPost("runs/{runId:guid}/payslips/publish"), HasPermission(Permissions.Payroll.PayslipPublish)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipDto>>>> Publish(Guid runId, CancellationToken ct) => (await service.PublishAsync(runId, ct)).ToActionResult();

    [HttpGet("runs/{runId:guid}/payslips"), HasPermission(Permissions.Payroll.PayslipViewAll)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayslipDto>>>> GetRunPayslips(Guid runId, [FromQuery] PayrollOutputQuery query, CancellationToken ct) => (await service.GetRunPayslipsAsync(runId, query, ct)).ToActionResult();

    [HttpGet("payslips/{id:guid}"), HasPermission(Permissions.Payroll.PayslipViewAll)]
    public async Task<ActionResult<ApiResponse<PayslipDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, false, ct)).ToActionResult();

    [HttpGet("payslips/{id:guid}/document"), HasPermission(Permissions.Payroll.PayslipViewAll)]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var result = await service.GetDocumentAsync(id, false, ct); if (!result.Succeeded) return result.ToErrorResult(); return Content(result.Value!, "text/html; charset=utf-8");
    }

    [HttpGet("runs/{runId:guid}/register"), HasPermission(Permissions.Payroll.RegisterView)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayrollRegisterRowDto>>>> Register(Guid runId, [FromQuery] PayrollOutputQuery query, CancellationToken ct) => (await service.GetRegisterAsync(runId, query, ct)).ToActionResult();

    [HttpGet("runs/{runId:guid}/register/export"), HasPermission(Permissions.Payroll.RegisterExport)]
    public async Task<IActionResult> Export(Guid runId, [FromQuery] PayrollOutputQuery query, CancellationToken ct)
    {
        var result = await service.ExportRegisterAsync(runId, query, ct); if (!result.Succeeded) return result.ToErrorResult(); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
}

[ApiController, Route("api/me/payslips")]
public sealed class MyPayslipsController(IPayrollOutputService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.PayslipViewOwn)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayslipDto>>>> Get([FromQuery] PayrollOutputQuery query, CancellationToken ct) => (await service.GetOwnAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.PayslipViewOwn)]
    public async Task<ActionResult<ApiResponse<PayslipDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetAsync(id, true, ct)).ToActionResult();

    [HttpGet("{id:guid}/document"), HasPermission(Permissions.Payroll.PayslipViewOwn)]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var result = await service.GetDocumentAsync(id, true, ct); if (!result.Succeeded) return result.ToErrorResult(); return Content(result.Value!, "text/html; charset=utf-8");
    }
}
