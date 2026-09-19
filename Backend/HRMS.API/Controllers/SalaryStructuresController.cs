using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/payroll/salary-structures")]
[Produces("application/json")]
public sealed class SalaryStructuresController(ISalaryStructureService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Payroll.SalaryStructureView)]
    public async Task<ActionResult<ApiResponse<PagedResult<SalaryStructureDto>>>> GetAll([FromQuery] SalaryStructureQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Payroll.SalaryStructureView)]
    public async Task<ActionResult<ApiResponse<SalaryStructureDto>>> GetById(Guid id, [FromQuery] DateOnly? effectiveOn, CancellationToken ct) => (await service.GetByIdAsync(id, effectiveOn, ct)).ToActionResult();

    [HttpPost]
    [HasPermission(Permissions.Payroll.SalaryStructureManage)]
    public async Task<ActionResult<ApiResponse<SalaryStructureDto>>> Create(SalaryStructureRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(GetById), x => new { id = x.Id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Payroll.SalaryStructureManage)]
    public async Task<ActionResult<ApiResponse<SalaryStructureDto>>> Update(Guid id, SalaryStructureRequest request, CancellationToken ct) => (await service.UpdateAsync(id, request, ct)).ToActionResult();

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.Payroll.SalaryStructureManage)]
    public async Task<ActionResult<ApiResponse<SalaryStructureDto>>> Activate(Guid id, [FromQuery] int? expectedConcurrencyVersion, CancellationToken ct) => (await service.SetActiveAsync(id, true, expectedConcurrencyVersion, ct)).ToActionResult();

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.Payroll.SalaryStructureManage)]
    public async Task<ActionResult<ApiResponse<SalaryStructureDto>>> Deactivate(Guid id, [FromQuery] int? expectedConcurrencyVersion, CancellationToken ct) => (await service.SetActiveAsync(id, false, expectedConcurrencyVersion, ct)).ToActionResult();

    [HttpGet("{id:guid}/history")]
    [HasPermission(Permissions.Payroll.SalaryStructureViewHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SalaryStructureHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/components")]
    [HasPermission(Permissions.Payroll.SalaryStructureManage)]
    public async Task<ActionResult<ApiResponse<SalaryStructureComponentDto>>> AddComponent(Guid id, SalaryStructureComponentRequest request, CancellationToken ct) => (await service.AddComponentAsync(id, request, ct)).ToActionResult();

    [HttpPut("{id:guid}/components/{componentId:guid}")]
    [HasPermission(Permissions.Payroll.SalaryStructureManage)]
    public async Task<ActionResult<ApiResponse<SalaryStructureComponentDto>>> UpdateComponent(Guid id, Guid componentId, SalaryStructureComponentRequest request, CancellationToken ct) => (await service.UpdateComponentAsync(id, componentId, request, ct)).ToActionResult();

    [HttpDelete("{id:guid}/components/{componentId:guid}")]
    [HasPermission(Permissions.Payroll.SalaryStructureManage)]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveComponent(Guid id, Guid componentId, CancellationToken ct) => (await service.RemoveComponentAsync(id, componentId, ct)).ToActionResult();
}
