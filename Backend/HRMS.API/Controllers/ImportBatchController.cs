using System.Security.Claims;
using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Employee CSV import. Manages import batches that track the lifecycle of a bulk upload.
/// </summary>
[ApiController]
[Route("api/import")]
[Produces("application/json")]
public class ImportBatchController : ControllerBase
{
    private readonly IImportBatchService _importBatchService;

    public ImportBatchController(IImportBatchService importBatchService)
    {
        _importBatchService = importBatchService;
    }

    private string ImportedBy => User.FindFirstValue(HrmsClaimTypes.Email) ?? "unknown";

    /// <summary>Returns all import batches for the current tenant.</summary>
    [HttpGet]
    [HasPermission(Permissions.Employee.Import)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ImportBatchDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ImportBatchDto>>>> GetBatches(
        CancellationToken cancellationToken)
    {
        var result = await _importBatchService.GetAsync(cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Returns a specific import batch.</summary>
    [HttpGet("{batchId:guid}")]
    [HasPermission(Permissions.Employee.Import)]
    [ProducesResponseType(typeof(ApiResponse<ImportBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ImportBatchDto>>> GetBatch(
        Guid batchId, CancellationToken cancellationToken)
    {
        var result = await _importBatchService.GetByIdAsync(batchId, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates a new import batch for a CSV file upload.</summary>
    [HttpPost]
    [HasPermission(Permissions.Employee.Import)]
    [ProducesResponseType(typeof(ApiResponse<ImportBatchDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<ImportBatchDto>>> CreateBatch(
        [FromQuery] string? fileName, CancellationToken cancellationToken)
    {
        var result = await _importBatchService.ImportAsync(fileName ?? "upload.csv", ImportedBy, cancellationToken);
        return result.ToCreatedResult(nameof(GetBatch), batch => new { batchId = batch.Id });
    }

    /// <summary>Deletes an import batch.</summary>
    [HttpDelete("{batchId:guid}")]
    [HasPermission(Permissions.Employee.Import)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBatch(
        Guid batchId, CancellationToken cancellationToken)
    {
        var result = await _importBatchService.DeleteAsync(batchId, cancellationToken);
        return result.ToActionResult();
    }
}
