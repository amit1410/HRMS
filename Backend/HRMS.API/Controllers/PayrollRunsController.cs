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
public sealed class PayrollRunsController(IPayrollRunService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.RunView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollRunDto>>>> Get([FromQuery] PayrollRunQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.RunView)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetByIdAsync(id, ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.RunManage)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Create(PayrollRunRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(GetById), x => new { id = x.Id });
    [HttpPost("{id:guid}/prepare"), HasPermission(Permissions.Payroll.RunPrepare)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Prepare(Guid id, [FromQuery] bool rebuild, CancellationToken ct) => (await service.PrepareAsync(id, rebuild, ct)).ToActionResult();
    [HttpGet("{id:guid}/employees"), HasPermission(Permissions.Payroll.RunView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollRunEmployeeDto>>>> Employees(Guid id, [FromQuery] PagedQuery query, CancellationToken ct) => (await service.GetEmployeesAsync(id, query, ct)).ToActionResult();
    [HttpPost("{id:guid}/transition"), HasPermission(Permissions.Payroll.RunManage)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Transition(Guid id, [FromQuery] PayrollRunStatus target, CancellationToken ct) => (await service.TransitionAsync(id, target, ct)).ToActionResult();
    [HttpGet("{id:guid}/history"), HasPermission(Permissions.Payroll.RunViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollRunHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
}
