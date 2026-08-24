using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Designations;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Designation (job title) endpoints. Same shape and same tenant guarantees as
/// <see cref="DepartmentsController"/>: the tenant is taken from the caller's token, never from the request.
/// </summary>
[ApiController]
[Route("api/designations")]
[Produces("application/json")]
public class DesignationsController : ControllerBase
{
    private readonly IDesignationService _designationService;

    public DesignationsController(IDesignationService designationService)
    {
        _designationService = designationService;
    }

    /// <summary>Lists the designations of the signed-in user's organization.</summary>
    [HttpGet]
    [HasPermission(Permissions.Designation.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DesignationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<DesignationDto>>>> GetAll(
        [FromQuery] DesignationQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _designationService.GetAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Returns one designation by id.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Designation.View)]
    [ProducesResponseType(typeof(ApiResponse<DesignationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DesignationDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _designationService.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates a designation.</summary>
    [HttpPost]
    [HasPermission(Permissions.Designation.Create)]
    [ProducesResponseType(typeof(ApiResponse<DesignationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DesignationDto>>> Create(
        [FromBody] DesignationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _designationService.CreateAsync(request, cancellationToken);
        return result.ToCreatedResult(nameof(GetById), dto => new { id = dto.Id });
    }

    /// <summary>Replaces a designation.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Designation.Edit)]
    [ProducesResponseType(typeof(ApiResponse<DesignationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DesignationDto>>> Update(
        Guid id,
        [FromBody] DesignationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _designationService.UpdateAsync(id, request, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Deletes a designation nobody holds.</summary>
    /// <remarks>If any employee still holds it the call returns 409 — mark it inactive instead.</remarks>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Designation.Delete)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _designationService.DeleteAsync(id, cancellationToken);
        return result.ToActionResult();
    }
}
