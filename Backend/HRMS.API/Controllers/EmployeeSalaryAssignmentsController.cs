using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/payroll/employee-salary-assignments")]
public sealed class EmployeeSalaryAssignmentsController(IEmployeeSalaryAssignmentService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Payroll.EmployeeSalaryView)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeSalaryAssignmentDto>>>> GetAll([FromQuery] EmployeeSalaryAssignmentQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryView)]
    public async Task<ActionResult<ApiResponse<EmployeeSalaryAssignmentDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetByIdAsync(id, ct)).ToActionResult();

    [HttpGet("by-employee/{employeeId:guid}")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryView)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeSalaryAssignmentDto>>>> ByEmployee(Guid employeeId, [FromQuery] EmployeeSalaryAssignmentQuery query, CancellationToken ct) => (await service.GetForEmployeeAsync(employeeId, query, ct)).ToActionResult();

    [HttpGet("by-employee/{employeeId:guid}/effective")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryView)]
    public async Task<ActionResult<ApiResponse<EmployeeSalaryAssignmentDto>>> Effective(Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct) => (await service.GetEffectiveAsync(employeeId, date, ct)).ToActionResult();

    [HttpPost]
    [HasPermission(Permissions.Payroll.EmployeeSalaryManage)]
    public async Task<ActionResult<ApiResponse<EmployeeSalaryAssignmentDto>>> Create(EmployeeSalaryAssignmentRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(GetById), x => new { id = x.Id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryManage)]
    public async Task<ActionResult<ApiResponse<EmployeeSalaryAssignmentDto>>> Update(Guid id, EmployeeSalaryAssignmentRequest request, CancellationToken ct) => (await service.UpdateAsync(id, request, ct)).ToActionResult();

    [HttpPost("{id:guid}/{actionName}")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryManage)]
    public async Task<ActionResult<ApiResponse<EmployeeSalaryAssignmentDto>>> SetActive(Guid id, string actionName, [FromQuery] int? expectedConcurrencyVersion, CancellationToken ct) => (await service.SetActiveAsync(id, string.Equals(actionName, "activate", StringComparison.OrdinalIgnoreCase), expectedConcurrencyVersion, ct)).ToActionResult();

    [HttpGet("{id:guid}/history")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryViewHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeSalaryAssignmentHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/components")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryManage)]
    public async Task<ActionResult<ApiResponse<EmployeeSalaryComponentDto>>> AddComponent(Guid id, EmployeeSalaryComponentRequest request, CancellationToken ct) => (await service.AddComponentAsync(id, request, ct)).ToActionResult();

    [HttpPut("{id:guid}/components/{componentId:guid}")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryManage)]
    public async Task<ActionResult<ApiResponse<EmployeeSalaryComponentDto>>> UpdateComponent(Guid id, Guid componentId, EmployeeSalaryComponentRequest request, CancellationToken ct) => (await service.UpdateComponentAsync(id, componentId, request, ct)).ToActionResult();

    [HttpDelete("{id:guid}/components/{componentId:guid}")]
    [HasPermission(Permissions.Payroll.EmployeeSalaryManage)]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveComponent(Guid id, Guid componentId, CancellationToken ct) => (await service.RemoveComponentAsync(id, componentId, ct)).ToActionResult();
}
