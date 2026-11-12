using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/employees/bulk-upload")]
public sealed class EmployeeBulkImportExecutionController(IEmployeeBulkImportExecutionService imports) : ControllerBase
{
    [HttpPost("prepare")]
    [HasPermission(Permissions.Employee.Import)]
    [HasPermission(Permissions.Employee.Create)]
    [HasPermission(Permissions.EmployeeSensitive.Edit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(IEmployeeBulkUploadValidationService.MaximumFileBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IEmployeeBulkUploadValidationService.MaximumFileBytes)]
    public async Task<ActionResult<ApiResponse<EmployeeBulkImportPreparationDto>>> Prepare([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (file is null || file.Length == 0) return BadRequest(new ApiResponse { Message = "An XLSX workbook is required." });
        if (file.Length > IEmployeeBulkUploadValidationService.MaximumFileBytes)
            return BadRequest(new ApiResponse { Message = "Workbook exceeds the 10 MB limit." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ApiResponse { Message = "Upload an .xlsx workbook." });
        await using var stream = file.OpenReadStream();
        return (await imports.PrepareAsync(stream, file.FileName, cancellationToken)).ToActionResult();
    }

    [HttpPost("confirm")]
    [HasPermission(Permissions.Employee.Import)]
    [HasPermission(Permissions.Employee.Create)]
    [HasPermission(Permissions.EmployeeSensitive.Edit)]
    [Consumes("application/json")]
    public async Task<ActionResult<ApiResponse<EmployeeBulkImportAcceptedDto>>> Confirm([FromBody] EmployeeBulkImportConfirmationRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!request.Confirm) return BadRequest(new ApiResponse { Message = "Explicit confirmation is required." });
        var result = await imports.ConfirmAsync(request.OperationId, request.ConfirmationToken, cancellationToken);
        if (!result.Succeeded) return result.ToErrorResult();
        return Accepted(result.Value!.StatusUrl, ApiResponse<EmployeeBulkImportAcceptedDto>.Ok(result.Value, result.Message));
    }

    [HttpGet("{operationId:guid}")]
    [HasPermission(Permissions.Employee.Import)]
    public async Task<ActionResult<ApiResponse<EmployeeBulkImportStatusDto>>> Status(Guid operationId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await imports.GetStatusAsync(operationId, cancellationToken)).ToActionResult();
    }

    [HttpGet("history")]
    [HasPermission(Permissions.Employee.Import)]
    public async Task<ActionResult<ApiResponse<EmployeeBulkImportHistoryDto>>> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        return (await imports.GetHistoryAsync(page, pageSize, cancellationToken)).ToActionResult();
    }
}

public sealed class EmployeeBulkImportConfirmationRequest
{
    public Guid OperationId { get; set; }

    public string ConfirmationToken { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}
