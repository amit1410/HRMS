using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/runs")]
public sealed class PayrollRunsController(IPayrollRunService service, IPayrollCalculationService calculation, IPayrollReadinessService readiness) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.RunView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollRunDto>>>> Get([FromQuery] PayrollRunQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.RunView)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetByIdAsync(id, ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.RunManage)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Create(PayrollRunRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(GetById), x => new { id = x.Id });
    [HttpPost("{id:guid}/prepare"), HasPermission(Permissions.Payroll.RunPrepare)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Prepare(Guid id, [FromQuery] bool rebuild, CancellationToken ct) => (await service.PrepareAsync(id, rebuild, ct)).ToActionResult();
    [HttpGet("{id:guid}/employees"), HasPermission(Permissions.Payroll.RunView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollRunEmployeeDto>>>> Employees(Guid id, [FromQuery] PagedQuery query, CancellationToken ct) => (await service.GetEmployeesAsync(id, query, ct)).ToActionResult();
    [HttpPost("{id:guid}/transition"), HasPermission(Permissions.Payroll.RunManage)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Transition(Guid id, [FromQuery] PayrollRunStatus target, CancellationToken ct) => (await service.TransitionAsync(id, target, ct)).ToActionResult();
    [HttpGet("{id:guid}/history"), HasPermission(Permissions.Payroll.RunViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollRunHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/calculate"), HasPermission(Permissions.Payroll.RunCalculate)] public async Task<ActionResult<ApiResponse<PayrollCalculationSummaryDto>>> Calculate(Guid id, CancellationToken ct) => (await calculation.CalculateAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/recalculate"), HasPermission(Permissions.Payroll.RunRecalculate)] public async Task<ActionResult<ApiResponse<PayrollCalculationSummaryDto>>> Recalculate(Guid id, CancellationToken ct) => (await calculation.RecalculateAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/results"), HasPermission(Permissions.Payroll.RunViewResults)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollResultDto>>>> Results(Guid id, [FromQuery] PayrollResultQuery query, CancellationToken ct) => (await calculation.GetResultsAsync(id, query, ct)).ToActionResult();
    [HttpGet("{id:guid}/results/{employeeId:guid}"), HasPermission(Permissions.Payroll.RunViewResults)] public async Task<ActionResult<ApiResponse<PayrollResultDto>>> Result(Guid id, Guid employeeId, CancellationToken ct) => (await calculation.GetResultAsync(id, employeeId, ct)).ToActionResult();
    [HttpGet("{id:guid}/calculation-errors"), HasPermission(Permissions.Payroll.RunViewResults)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollCalculationErrorDto>>>> Errors(Guid id, CancellationToken ct) => (await calculation.GetErrorsAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/readiness"), HasPermission(Permissions.Payroll.RunView)] public async Task<ActionResult<ApiResponse<PayrollReadinessDto>>> Readiness(Guid id, CancellationToken ct) => (await readiness.CheckAsync(id, ct)).ToActionResult();
}
