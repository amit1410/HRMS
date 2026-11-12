using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/employees/bulk-upload")]
public sealed class EmployeeBulkUploadValidationController(IEmployeeBulkUploadValidationService validation) : ControllerBase
{
    [HttpPost("validate")]
    [HasPermission(Permissions.Employee.Import)]
    [HasPermission(Permissions.EmployeeSensitive.Edit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(IEmployeeBulkUploadValidationService.MaximumFileBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IEmployeeBulkUploadValidationService.MaximumFileBytes)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Validate([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (file is null || file.Length == 0) return BadRequest(new ApiResponse { Message = "An XLSX workbook is required." });
        if (file.Length > IEmployeeBulkUploadValidationService.MaximumFileBytes)
            return BadRequest(new ApiResponse { Message = "Workbook exceeds the 10 MB limit." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ApiResponse { Message = "Upload an .xlsx workbook." });
        await using var stream = file.OpenReadStream();
        var result = await validation.ValidateAsync(stream, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }
}
