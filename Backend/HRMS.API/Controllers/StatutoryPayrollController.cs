using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class StatutoryPayrollController(IStatutoryPayrollService service) : ControllerBase
{
    [HttpGet("api/payroll/statutory-configurations")]
    [HasPermission(Permissions.Payroll.StatutoryView)]
    public async Task<ActionResult<ApiResponse<PagedResult<StatutoryConfigurationDto>>>> Configurations([FromQuery] StatutoryConfigurationQuery query, CancellationToken ct) => (await service.GetConfigurationsAsync(query, ct)).ToActionResult();

    [HttpGet("api/payroll/statutory-configurations/{id:guid}")]
    [HasPermission(Permissions.Payroll.StatutoryView)]
    public async Task<ActionResult<ApiResponse<StatutoryConfigurationDto>>> Configuration(Guid id, CancellationToken ct) => (await service.GetConfigurationAsync(id, ct)).ToActionResult();

    [HttpPost("api/payroll/statutory-configurations")]
    [HasPermission(Permissions.Payroll.StatutoryManage)]
    public async Task<ActionResult<ApiResponse<StatutoryConfigurationDto>>> CreateConfiguration(StatutoryConfigurationRequest request, CancellationToken ct) => (await service.CreateConfigurationAsync(request, ct)).ToCreatedResult(nameof(Configuration), x => new { id = x.Id });

    [HttpPost("api/payroll/statutory-configurations/{id:guid}/versions")]
    [HasPermission(Permissions.Payroll.StatutoryManage)]
    public async Task<ActionResult<ApiResponse<StatutoryConfigurationVersionDto>>> AddVersion(Guid id, StatutoryConfigurationVersionRequest request, CancellationToken ct) => (await service.AddVersionAsync(id, request, ct)).ToActionResult();

    [HttpGet("api/payroll/employees/{employeeId:guid}/statutory-profile")]
    [HasPermission(Permissions.Payroll.EmployeeStatutoryView)]
    public async Task<ActionResult<ApiResponse<EmployeeStatutoryProfileDto>>> Profile(Guid employeeId, CancellationToken ct) => (await service.GetProfileAsync(employeeId, ct)).ToActionResult();

    [HttpPut("api/payroll/employees/{employeeId:guid}/statutory-profile")]
    [HasPermission(Permissions.Payroll.EmployeeStatutoryManage)]
    public async Task<ActionResult<ApiResponse<EmployeeStatutoryProfileDto>>> SaveProfile(Guid employeeId, EmployeeStatutoryProfileRequest request, CancellationToken ct) => (await service.SaveProfileAsync(employeeId, request, ct)).ToActionResult();

    [HttpGet("api/payroll/runs/{runId:guid}/results/{employeeId:guid}/statutory")]
    [HasPermission(Permissions.Payroll.StatutoryView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollStatutoryResultDto>>>> Results(Guid runId, Guid employeeId, CancellationToken ct) => (await service.GetResultsAsync(runId, employeeId, ct)).ToActionResult();
}
