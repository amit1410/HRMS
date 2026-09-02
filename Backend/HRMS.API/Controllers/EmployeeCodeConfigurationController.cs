using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/employee-code-configuration")]
[Produces("application/json")]
public sealed class EmployeeCodeConfigurationController : ControllerBase
{
    private readonly IEmployeeCodeConfigurationService _service;
    public EmployeeCodeConfigurationController(IEmployeeCodeConfigurationService service) => _service = service;

    [HttpGet]
    [HasPermission(Permissions.EmployeeCodeConfiguration.View)]
    public async Task<ActionResult<ApiResponse<EmployeeCodeConfigurationDto>>> Get(CancellationToken cancellationToken) =>
        (await _service.GetAsync(cancellationToken)).ToActionResult();

    [HttpPut]
    [HasPermission(Permissions.EmployeeCodeConfiguration.Manage)]
    public async Task<ActionResult<ApiResponse<EmployeeCodeConfigurationDto>>> Save([FromBody] EmployeeCodeConfigurationRequest request, CancellationToken cancellationToken) =>
        (await _service.SaveAsync(request, cancellationToken)).ToActionResult();

    [HttpGet("rules")]
    [HasPermission(Permissions.EmployeeCodeConfiguration.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeCodeRuleDto>>>> GetRules(CancellationToken cancellationToken) =>
        (await _service.GetRulesAsync(cancellationToken)).ToActionResult();

    [HttpGet("rules/{id:guid}")]
    [HasPermission(Permissions.EmployeeCodeConfiguration.View)]
    public async Task<ActionResult<ApiResponse<EmployeeCodeRuleDto>>> GetRule(Guid id, CancellationToken cancellationToken) =>
        (await _service.GetRuleAsync(id, cancellationToken)).ToActionResult();

    [HttpPut("rules/{id:guid?}")]
    [HasPermission(Permissions.EmployeeCodeConfiguration.Manage)]
    public async Task<ActionResult<ApiResponse<EmployeeCodeRuleDto>>> SaveRule(Guid? id, [FromBody] EmployeeCodeRuleRequest request, CancellationToken cancellationToken) =>
        (await _service.SaveRuleAsync(id, request, cancellationToken)).ToActionResult();

    [HttpPost("rules")]
    [HasPermission(Permissions.EmployeeCodeConfiguration.Manage)]
    public async Task<ActionResult<ApiResponse<EmployeeCodeRuleDto>>> CreateRule([FromBody] EmployeeCodeRuleRequest request, CancellationToken cancellationToken) =>
        (await _service.SaveRuleAsync(null, request, cancellationToken)).ToActionResult();

    [HttpDelete("rules/{id:guid}")]
    [HasPermission(Permissions.EmployeeCodeConfiguration.Manage)]
    public async Task<ActionResult<ApiResponse<EmployeeCodeRuleDto>>> DeleteRule(Guid id, CancellationToken cancellationToken) =>
        (await _service.SoftDeleteRuleAsync(id, cancellationToken)).ToActionResult();
}
