using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/payroll/salary-components")]
[Produces("application/json")]
public sealed class SalaryComponentsController(ISalaryComponentService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Payroll.SalaryComponentView)]
    public async Task<ActionResult<ApiResponse<PagedResult<SalaryComponentDto>>>> GetAll([FromQuery] SalaryComponentQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Payroll.SalaryComponentView)]
    public async Task<ActionResult<ApiResponse<SalaryComponentDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetByIdAsync(id, ct)).ToActionResult();

    [HttpPost]
    [HasPermission(Permissions.Payroll.SalaryComponentManage)]
    public async Task<ActionResult<ApiResponse<SalaryComponentDto>>> Create(SalaryComponentRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(GetById), x => new { id = x.Id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Payroll.SalaryComponentManage)]
    public async Task<ActionResult<ApiResponse<SalaryComponentDto>>> Update(Guid id, SalaryComponentRequest request, CancellationToken ct) => (await service.UpdateAsync(id, request, ct)).ToActionResult();

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.Payroll.SalaryComponentManage)]
    public async Task<ActionResult<ApiResponse<SalaryComponentDto>>> Activate(Guid id, [FromQuery] int? expectedConcurrencyVersion, CancellationToken ct) => (await service.SetActiveAsync(id, true, expectedConcurrencyVersion, ct)).ToActionResult();

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.Payroll.SalaryComponentManage)]
    public async Task<ActionResult<ApiResponse<SalaryComponentDto>>> Deactivate(Guid id, [FromQuery] int? expectedConcurrencyVersion, CancellationToken ct) => (await service.SetActiveAsync(id, false, expectedConcurrencyVersion, ct)).ToActionResult();

    [HttpGet("{id:guid}/history")]
    [HasPermission(Permissions.Payroll.SalaryComponentViewHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SalaryComponentHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
}
