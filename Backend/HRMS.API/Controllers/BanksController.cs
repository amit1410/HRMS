using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Banks;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Bank master endpoints. The controller binds input, checks the permission, delegates to
/// <see cref="IBankService"/> and maps the outcome — every rule (tenant scoping, uniqueness, whether a bank
/// may be deleted) lives in the Application layer.
/// <para>
/// No endpoint takes a tenant id. The tenant comes from the caller's token, so there is nothing here a
/// client could tamper with to reach another organization's data.
/// </para>
/// </summary>
[ApiController]
[Route("api/banks")]
[Produces("application/json")]
public class BanksController : ControllerBase
{
    private readonly IBankService _bankService;
    private readonly IBankImportService _bankImportService;

    public BanksController(IBankService bankService, IBankImportService bankImportService)
    {
        _bankService = bankService;
        _bankImportService = bankImportService;
    }

    /// <summary>Lists the banks of the signed-in user's organization.</summary>
    /// <remarks>Supports search, an active/inactive filter, paging and sorting.</remarks>
    [HttpGet]
    [HasPermission(Permissions.BankMaster.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BankDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<BankDto>>>> GetAll(
        [FromQuery] BankQuery query, CancellationToken cancellationToken)
    {
        var result = await _bankService.GetAsync(query, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Exports banks (respecting the current filters) as CSV or XLSX.</summary>
    [HttpGet("export")]
    [HasPermission(Permissions.BankMaster.Export)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromQuery] BankQuery query, [FromQuery] string format = "csv", CancellationToken cancellationToken = default)
    {
        var result = await _bankService.GetAllForExportAsync(query, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return Unauthorized(ApiResponse<IReadOnlyList<BankDto>>.Fail(result.Message));
        }

        var xlsx = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        var bytes = _bankImportService.BuildExport(result.Value, xlsx ? BankFileFormat.Xlsx : BankFileFormat.Csv);
        return File(
            bytes,
            xlsx ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv",
            xlsx ? "banks.xlsx" : "banks.csv");
    }

    /// <summary>Returns one bank by id.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.BankMaster.View)]
    [ProducesResponseType(typeof(ApiResponse<BankDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BankDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bankService.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates a bank.</summary>
    /// <remarks>Code and name are unique within the organization; a duplicate returns 409.</remarks>
    [HttpPost]
    [HasPermission(Permissions.BankMaster.Create)]
    [ProducesResponseType(typeof(ApiResponse<BankDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BankDto>>> Create(
        [FromBody] BankRequest request, CancellationToken cancellationToken)
    {
        var result = await _bankService.CreateAsync(request, cancellationToken);
        return result.ToCreatedResult(nameof(GetById), dto => new { id = dto.Id });
    }

    /// <summary>Replaces a bank.</summary>
    /// <remarks>A full replacement: an omitted optional field is cleared rather than left unchanged.</remarks>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.BankMaster.Edit)]
    [ProducesResponseType(typeof(ApiResponse<BankDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BankDto>>> Update(
        Guid id, [FromBody] BankRequest request, CancellationToken cancellationToken)
    {
        var result = await _bankService.UpdateAsync(id, request, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Activates a bank so it can be assigned to employee bank records again.</summary>
    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.BankMaster.Activate)]
    [ProducesResponseType(typeof(ApiResponse<BankDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BankDto>>> Activate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bankService.SetActiveAsync(id, isActive: true, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Deactivates a bank. Existing employee bank references are preserved.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.BankMaster.Activate)]
    [ProducesResponseType(typeof(ApiResponse<BankDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BankDto>>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bankService.SetActiveAsync(id, isActive: false, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Deletes an unreferenced bank.</summary>
    /// <remarks>A bank referenced by any employee bank record returns 409 — deactivate it instead.</remarks>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.BankMaster.Edit)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bankService.DeleteAsync(id, cancellationToken);
        return result.ToActionResult();
    }
}
