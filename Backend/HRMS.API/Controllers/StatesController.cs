using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.States;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// State reference data endpoints. States are global (not tenant-scoped).
/// </summary>
[ApiController]
[Route("api/states")]
[Produces("application/json")]
[HasPermission(Permissions.Geography.View)]
public class StatesController : ControllerBase
{
    private readonly IStateService _stateService;

    public StatesController(IStateService stateService)
    {
        _stateService = stateService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<StateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<StateDto>>>> GetAll(
        [FromQuery] StateQuery query, CancellationToken cancellationToken)
    {
        var result = await _stateService.GetAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<StateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<StateDto>>> GetById(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _stateService.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost]
    [HasPermission(Permissions.Geography.Manage)]
    [ProducesResponseType(typeof(ApiResponse<StateDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<StateDto>>> Create(
        [FromBody] StateRequest request, CancellationToken cancellationToken)
    {
        var result = await _stateService.CreateAsync(request, cancellationToken);
        return result.ToCreatedResult(nameof(GetById), dto => new { id = dto.Id });
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Geography.Manage)]
    [ProducesResponseType(typeof(ApiResponse<StateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<StateDto>>> Update(
        Guid id, [FromBody] StateRequest request, CancellationToken cancellationToken)
    {
        var result = await _stateService.UpdateAsync(id, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Geography.Manage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _stateService.DeleteAsync(id, cancellationToken);
        return result.ToActionResult();
    }
}
