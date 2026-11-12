using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/employees/bulk-upload")]
[Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
public sealed class EmployeeBulkUploadTemplateController(IEmployeeBulkUploadTemplateService templates) : ControllerBase
{
    [HttpGet("template")]
    [HasPermission(Permissions.Employee.Import)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Download(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await templates.GenerateAsync(cancellationToken);
        if (!result.Succeeded)
            return result.ToErrorResult();

        return File(result.Value!, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "employee-bulk-upload-template.xlsx");
    }
}
