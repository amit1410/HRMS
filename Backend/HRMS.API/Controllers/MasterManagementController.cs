using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Masters;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/masters")]
[Produces("application/json")]
public sealed class MasterManagementController : ControllerBase
{
    private readonly IMasterManagementService _service;

    public MasterManagementController(IMasterManagementService service) => _service = service;

    [HttpGet("{kind}")]
    [HasPermission(Permissions.Geography.View)]
    public async Task<ActionResult<ApiResponse<MasterManagementPage>>> Get(string kind, [FromQuery] MasterManagementQuery query, CancellationToken cancellationToken) =>
        (await _service.GetAsync(kind, query, cancellationToken)).ToActionResult();

    [HttpGet("{kind}/{id:guid}")]
    [HasPermission(Permissions.Geography.View)]
    public async Task<ActionResult<ApiResponse<MasterManagementRecordDto>>> GetById(string kind, Guid id, CancellationToken cancellationToken) =>
        (await _service.GetByIdAsync(kind, id, cancellationToken)).ToActionResult();

    [HttpPost("{kind}")]
    [HasPermission(Permissions.Geography.Manage)]
    public async Task<ActionResult<ApiResponse<MasterManagementRecordDto>>> Create(string kind, MasterManagementRequest request, CancellationToken cancellationToken) =>
        (await _service.CreateAsync(kind, request, cancellationToken)).ToActionResult();

    [HttpPut("{kind}/{id:guid}")]
    [HasPermission(Permissions.Geography.Manage)]
    public async Task<ActionResult<ApiResponse<MasterManagementRecordDto>>> Update(string kind, Guid id, MasterManagementRequest request, CancellationToken cancellationToken) =>
        (await _service.UpdateAsync(kind, id, request, cancellationToken)).ToActionResult();

    [HttpDelete("{kind}/{id:guid}")]
    [HasPermission(Permissions.Geography.Manage)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(string kind, Guid id, CancellationToken cancellationToken) =>
        (await _service.DeleteAsync(kind, id, cancellationToken)).ToActionResult();
}
