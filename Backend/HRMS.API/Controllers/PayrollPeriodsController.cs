using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/periods")]
public sealed class PayrollPeriodsController(IPayrollPeriodService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.PeriodView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollPeriodDto>>>> Get([FromQuery] PayrollPeriodQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.PeriodView)] public async Task<ActionResult<ApiResponse<PayrollPeriodDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetByIdAsync(id, ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.PeriodManage)] public async Task<ActionResult<ApiResponse<PayrollPeriodDto>>> Create(PayrollPeriodRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(GetById), x => new { id = x.Id });
    [HttpPut("{id:guid}"), HasPermission(Permissions.Payroll.PeriodManage)] public async Task<ActionResult<ApiResponse<PayrollPeriodDto>>> Update(Guid id, PayrollPeriodRequest request, CancellationToken ct) => (await service.UpdateAsync(id, request, ct)).ToActionResult();
    [HttpPost("{id:guid}/{actionName}"), HasPermission(Permissions.Payroll.PeriodManage)] public async Task<ActionResult<ApiResponse<PayrollPeriodDto>>> Transition(Guid id, string actionName, [FromQuery] int? expectedConcurrencyVersion, CancellationToken ct) => (await service.TransitionAsync(id, actionName, expectedConcurrencyVersion, ct)).ToActionResult();
    [HttpGet("{id:guid}/history"), HasPermission(Permissions.Payroll.PeriodManage)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollPeriodHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
}
