using System.Security.Claims;
using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Banks;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Bank master bulk import (CSV/XLSX), downloadable template and import history. Validation is separate from
/// the confirmed import, so a caller previews the outcome before anything is written.
/// </summary>
[ApiController]
[Route("api/bank-import")]
[Produces("application/json")]
public sealed class BankImportController : ControllerBase
{
    private const int MaxUploadBytes = 5 * 1024 * 1024;

    private readonly IBankImportService _service;

    public BankImportController(IBankImportService service) => _service = service;

    /// <summary>Downloads an empty import template (CSV by default, or XLSX with <c>?format=xlsx</c>).</summary>
    [HttpGet("template")]
    [HasPermission(Permissions.BankMaster.Import)]
    public IActionResult Template([FromQuery] string format = "csv")
    {
        var xlsx = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        var bytes = _service.BuildTemplate(xlsx ? BankFileFormat.Xlsx : BankFileFormat.Csv);
        return File(
            bytes,
            xlsx ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv",
            xlsx ? "bank-master-template.xlsx" : "bank-master-template.csv");
    }

    /// <summary>Validates an uploaded file and returns a per-row preview, writing nothing.</summary>
    [HttpPost("validate")]
    [HasPermission(Permissions.BankMaster.Import)]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<ApiResponse<BankImportPreview>>> Validate(
        [FromForm] IFormFile? file,
        [FromForm] BankImportMode mode = BankImportMode.CreateOnly,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponse<BankImportPreview>.Fail("Select a non-empty CSV or XLSX file."));
        }

        await using var stream = file.OpenReadStream();
        return (await _service.ValidateAsync(mode, stream, file.FileName, ct)).ToActionResult();
    }

    /// <summary>Applies a validated set of rows in a single transaction.</summary>
    [HttpPost("confirm")]
    [HasPermission(Permissions.BankMaster.Import)]
    public async Task<ActionResult<ApiResponse<BankImportResult>>> Confirm(
        [FromBody] BankImportConfirmRequest request, CancellationToken ct)
    {
        var importedBy = User.FindFirstValue(HrmsClaimTypes.Email) ?? "unknown";
        return (await _service.ConfirmAsync(request, importedBy, ct)).ToActionResult();
    }

    /// <summary>Returns the Bank master import history for the current tenant, newest first.</summary>
    [HttpGet("history")]
    [HasPermission(Permissions.BankMaster.Import)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BankImportHistoryDto>>>> History(CancellationToken ct)
    {
        return (await _service.GetHistoryAsync(ct)).ToActionResult();
    }
}
