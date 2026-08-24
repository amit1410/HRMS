using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Employee endpoints.
/// <para>
/// Note the permissions: viewing, creating, editing, deleting and exporting are five separate grants.
/// Export is its own permission because a CSV of the whole directory — including dates of birth, phone
/// numbers and addresses — is a materially different capability from paging through the list on screen.
/// </para>
/// </summary>
[ApiController]
[Route("api/employees")]
[Produces("application/json")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    /// <summary>Lists the employees of the signed-in user's organization.</summary>
    /// <remarks>
    /// Search matches employee code, first name, last name and email. Results can be filtered by department,
    /// designation, status or reporting manager, and sorted by any of the documented fields.
    /// </remarks>
    [HttpGet]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EmployeeListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeListItemDto>>>> GetAll(
        [FromQuery] EmployeeQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _employeeService.GetAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Returns one employee, including department, designation and reporting manager names.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _employeeService.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates an employee.</summary>
    /// <remarks>
    /// The department, designation and manager ids must belong to the caller's own organization; one that
    /// does not is reported as nonexistent. Employee code and email are unique within the organization.
    /// </remarks>
    [HttpPost]
    [HasPermission(Permissions.Employee.Create)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> Create(
        [FromBody] EmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _employeeService.CreateAsync(request, cancellationToken);
        return result.ToCreatedResult(nameof(GetById), dto => new { id = dto.Id });
    }

    /// <summary>Replaces an employee record.</summary>
    /// <remarks>
    /// A full replacement. Changing the reporting manager is checked against the existing hierarchy: an
    /// employee may not report to themselves or to anyone who already reports up through them.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> Update(
        Guid id,
        [FromBody] EmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _employeeService.UpdateAsync(id, request, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Deletes an employee record.</summary>
    /// <remarks>
    /// For correcting a mistaken record. Someone who has left the organization should be updated to
    /// Resigned or Terminated instead, which keeps their history. Deleting a manager who still has direct
    /// reports returns 409.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Employee.Delete)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _employeeService.DeleteAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Downloads the filtered employee list as a CSV file.</summary>
    /// <remarks>
    /// Accepts the same filters as the list endpoint (paging aside — an export is the whole filtered set).
    /// A result larger than the export limit is refused with 400 rather than truncated, so a file is never
    /// quietly missing rows.
    /// </remarks>
    [HttpGet("export")]
    [HasPermission(Permissions.Employee.Export)]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromQuery] EmployeeQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _employeeService.ExportAsync(query, cancellationToken);
        if (!result.Succeeded)
        {
            // Errors keep the standard JSON envelope even though success is a file.
            return result.ToErrorResult();
        }

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
