using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-balances/import"), Produces("application/json")]
[Authorize]
public sealed class LeaveBalanceImportsController(ILeaveBalanceImportService service) : ControllerBase
{
    [HttpGet("template"), HasPermission(Permissions.Leave.BalanceImport)]
    public IActionResult Template() => File("EmployeeCode,LeaveTypeCode,LeavePeriod,OpeningBalance,EffectiveDate,Remarks\r\n", "text/csv", "leave-balance-import-template.csv");

    [HttpPost("validate"), HasPermission(Permissions.Leave.BalanceImport)]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<ApiResponse<LeaveBalanceImportBatchDto>>> Validate(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Result<LeaveBalanceImportBatchDto>.Invalid("file", "A non-empty CSV file is required.").ToActionResult();
        await using var stream = file.OpenReadStream();
        return (await service.ValidateAsync(file.FileName, stream, ct)).ToActionResult();
    }

    [HttpPost("{batchId:guid}/commit"), HasPermission(Permissions.Leave.BalanceImport)]
    public async Task<ActionResult<ApiResponse<LeaveBalanceImportBatchDto>>> Commit(Guid batchId, CancellationToken ct) => (await service.CommitAsync(batchId, ct)).ToActionResult();

    [HttpGet("{batchId:guid}"), HasPermission(Permissions.Leave.BalanceImport)]
    public async Task<ActionResult<ApiResponse<LeaveBalanceImportBatchDto>>> Get(Guid batchId, CancellationToken ct) => (await service.GetAsync(batchId, ct)).ToActionResult();

    [HttpGet("{batchId:guid}/errors"), HasPermission(Permissions.Leave.BalanceImport)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveBalanceImportRowDto>>>> Errors(Guid batchId, CancellationToken ct) => (await service.ErrorsAsync(batchId, ct)).ToActionResult();

    [HttpGet("history"), HasPermission(Permissions.Leave.BalanceViewImportHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaveBalanceImportBatchDto>>>> History(CancellationToken ct) => (await service.HistoryAsync(ct)).ToActionResult();
}
