using System.Security.Claims;
using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Masters;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/master-import")]
[Produces("application/json")]
public sealed class MasterImportController : ControllerBase
{
    private readonly IMasterImportService _service;
    public MasterImportController(IMasterImportService service) => _service = service;

    [HttpGet("{kind}/template")]
    [HasPermission(Permissions.Geography.Manage)]
    public IActionResult Template(string kind) => File(_service.Template(kind), "text/csv", $"{kind}-template.csv");

    [HttpPost("{kind}/validate")]
    [HasPermission(Permissions.Geography.Manage)]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<MasterImportPreview>>> Validate(string kind, [FromForm] IFormFile? file, [FromForm] MasterImportMode mode = MasterImportMode.CreateOnly, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return BadRequest(ApiResponse<MasterImportPreview>.Fail("Select a non-empty CSV file."));
        await using var stream = file.OpenReadStream();
        try { return (await _service.ValidateAsync(kind, mode, stream, file.FileName, ct)).ToActionResult(); }
        catch (InvalidDataException ex) { return BadRequest(ApiResponse<MasterImportPreview>.Fail(ex.Message)); }
    }

    [HttpPost("{kind}/confirm")]
    [HasPermission(Permissions.Geography.Manage)]
    public async Task<ActionResult<ApiResponse<MasterImportResult>>> Confirm(string kind, MasterImportConfirmRequest request, CancellationToken ct)
    {
        if (!kind.Equals(request.MasterType, StringComparison.OrdinalIgnoreCase)) return BadRequest(ApiResponse<MasterImportResult>.Fail("Master type does not match the route."));
        var importedBy = User.FindFirstValue(HrmsClaimTypes.Email) ?? "unknown";
        return (await _service.ConfirmAsync(request, importedBy, ct)).ToActionResult();
    }
}
