using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Departments;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Department endpoints. The controller binds input, checks the permission, delegates to
/// <see cref="IDepartmentService"/> and maps the outcome — every rule (tenant scoping, uniqueness, whether a
/// department may be deleted) lives in the Application layer.
/// <para>
/// No endpoint takes a tenant id. The tenant comes from the caller's token, so there is nothing here a
/// client could tamper with to reach another organization's data.
/// </para>
/// </summary>
[ApiController]
[Route("api/departments")]
[Produces("application/json")]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departmentService;

    public DepartmentsController(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    /// <summary>Lists the departments of the signed-in user's organization.</summary>
    /// <remarks>Supports search, an active/inactive filter, paging and sorting by code, name, employee count, status or creation date.</remarks>
    [HttpGet]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DepartmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<DepartmentDto>>>> GetAll(
        [FromQuery] DepartmentQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _departmentService.GetAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Returns one department by id.</summary>
    /// <remarks>An id belonging to another organization returns 404, not 403: the API never confirms that a record exists elsewhere.</remarks>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _departmentService.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates a department.</summary>
    /// <remarks>Code and name are unique within the organization; a duplicate returns 409.</remarks>
    [HttpPost]
    [HasPermission(Permissions.Department.Create)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> Create(
        [FromBody] DepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _departmentService.CreateAsync(request, cancellationToken);
        return result.ToCreatedResult(nameof(GetById), dto => new { id = dto.Id });
    }

    /// <summary>Replaces a department.</summary>
    /// <remarks>A full replacement: an omitted optional field is cleared rather than left unchanged.</remarks>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Department.Edit)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> Update(
        Guid id,
        [FromBody] DepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _departmentService.UpdateAsync(id, request, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Deletes an empty department.</summary>
    /// <remarks>A department that still has employees returns 409 — retire it by setting IsActive to false instead, so employee history keeps its unit.</remarks>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Department.Delete)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _departmentService.DeleteAsync(id, cancellationToken);
        return result.ToActionResult();
    }
}
