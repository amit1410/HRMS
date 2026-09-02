using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Masters;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Lookup endpoints for all organizational master data. Returns lightweight
/// <see cref="MasterLookupDto"/> lists suitable for dropdown/population.
/// </summary>
[ApiController]
[Route("api/master-data")]
[Produces("application/json")]
public class MasterLookupController : ControllerBase
{
    private readonly IMasterLookupService _masterLookupService;

    public MasterLookupController(IMasterLookupService masterLookupService)
    {
        _masterLookupService = masterLookupService;
    }

    [HttpGet("holding-companies")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetHoldingCompanies(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetHoldingCompaniesAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("lines-of-business")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetLinesOfBusiness(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetLinesOfBusinessAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("organisations")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetOrganisations(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetOrganisationsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("departments")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetDepartments(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetDepartmentsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("banks")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetBanks(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetBanksAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("sub-departments")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetSubDepartments(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetSubDepartmentsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("sections")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetSections(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetSectionsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("sub-sections")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetSubSections(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetSubSectionsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("functions")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetFunctions(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetFunctionsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("sub-functions")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetSubFunctions(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetSubFunctionsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("grades")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetGrades(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetGradesAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("designations")]
    [HasPermission(Permissions.Designation.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetDesignations(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetDesignationsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("employee-types")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetEmployeeTypes(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetEmployeeTypesAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("work-locations")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetWorkLocations(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetWorkLocationsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("cost-centers")]
    [HasPermission(Permissions.Department.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetCostCenters(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetCostCentersAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }

    [HttpGet("position-change-reasons")]
    [HasPermission(Permissions.EmploymentHistory.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterLookupDto>>>> GetPositionChangeReasons(
        [FromQuery] MasterLookupQuery query, CancellationToken ct)
    {
        var result = await _masterLookupService.GetPositionChangeReasonsAsync(query, ct);
        return Ok(ApiResponse<IReadOnlyList<MasterLookupDto>>.Ok(result));
    }
}
