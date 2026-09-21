using System.Globalization;
using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.AccountEmployeeLinks;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll")]
public sealed class ReimbursementsController(IReimbursementService service, IAccountEmployeeLinkService links, ITenantContext tenant) : ControllerBase
{
    [HttpGet("reimbursement-categories"), HasPermission(Permissions.Payroll.ReimbursementsView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<ReimbursementCategoryDto>>>> Categories(CancellationToken ct) => (await service.GetCategoriesAsync(ct)).ToActionResult();
    [HttpPost("reimbursement-categories"), HasPermission(Permissions.Payroll.ReimbursementsManageCategories)] public async Task<ActionResult<ApiResponse<ReimbursementCategoryDto>>> CreateCategory(ReimbursementCategoryRequest request, CancellationToken ct) => (await service.CreateCategoryAsync(request, ct)).ToActionResult();
    [HttpPut("reimbursement-categories/{id:guid}"), HasPermission(Permissions.Payroll.ReimbursementsManageCategories)] public async Task<ActionResult<ApiResponse<ReimbursementCategoryDto>>> UpdateCategory(Guid id, ReimbursementCategoryRequest request, CancellationToken ct) => (await service.UpdateCategoryAsync(id, request, ct)).ToActionResult();
    [HttpPost("reimbursement-categories/{id:guid}/versions"), HasPermission(Permissions.Payroll.ReimbursementsManageCategories)] public async Task<ActionResult<ApiResponse<ReimbursementCategoryDto>>> CreatePolicy(Guid id, ReimbursementPolicyVersionRequest request, CancellationToken ct) => (await service.CreatePolicyVersionAsync(id, request, ct)).ToActionResult();
    [HttpGet("reimbursements"), HasPermission(Permissions.Payroll.ReimbursementsView)] public async Task<ActionResult<ApiResponse<PagedResult<ReimbursementClaimDto>>>> Claims([FromQuery] ReimbursementClaimQuery query, CancellationToken ct) => (await service.GetClaimsAsync(query, ct)).ToActionResult();
    [HttpGet("reimbursements/{id:guid}"), HasPermission(Permissions.Payroll.ReimbursementsView)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Claim(Guid id, CancellationToken ct) => (await service.GetClaimAsync(id, ct)).ToActionResult();
    [HttpGet("reimbursements/{id:guid}/history"), HasPermission(Permissions.Payroll.ReimbursementsViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<ReimbursementHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
    [HttpPost("reimbursements"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Create(ReimbursementClaimRequest request, CancellationToken ct) => (await service.CreateClaimAsync(request, ct)).ToActionResult();
    [HttpPut("reimbursements/{id:guid}"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Update(Guid id, ReimbursementClaimRequest request, CancellationToken ct) => (await service.UpdateClaimAsync(id, request, ct)).ToActionResult();
    [HttpPost("reimbursements/{id:guid}/submit"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("reimbursements/{id:guid}/approve"), HasPermission(Permissions.Payroll.ReimbursementsApprove)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Approve(Guid id, ReimbursementApprovalRequest request, CancellationToken ct) => (await service.ApproveAsync(id, request, ct)).ToActionResult();
    [HttpPost("reimbursements/{id:guid}/reject"), HasPermission(Permissions.Payroll.ReimbursementsApprove)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Reject(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.RejectAsync(id, reason, ct)).ToActionResult();
    [HttpPost("reimbursements/{id:guid}/settle-manually"), HasPermission(Permissions.Payroll.ReimbursementsSettle)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Settle(Guid id, ReimbursementSettlementRequest request, CancellationToken ct) => (await service.SettleManuallyAsync(id, request, ct)).ToActionResult();
    [HttpPost("reimbursements/{id:guid}/cancel"), HasPermission(Permissions.Payroll.ReimbursementsCancel)] public async Task<ActionResult<ApiResponse<ReimbursementClaimDto>>> Cancel(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.CancelAsync(id, reason, ct)).ToActionResult();
    [HttpPost("reimbursements/{id:guid}/attachments"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<ActionResult<ApiResponse<ReimbursementAttachmentDto>>> Attachment(Guid id, ReimbursementAttachmentRequest request, CancellationToken ct) => (await service.AddAttachmentAsync(id, request, ct)).ToActionResult();
    [HttpGet("reimbursements/register"), HasPermission(Permissions.Payroll.ReimbursementsView)] public async Task<ActionResult<ApiResponse<PagedResult<ReimbursementRegisterRowDto>>>> Register([FromQuery] ReimbursementClaimQuery query, CancellationToken ct) => (await service.GetRegisterAsync(query, ct)).ToActionResult();
    [HttpGet("reimbursements/register/export.csv"), HasPermission(Permissions.Payroll.ReimbursementsView)] public async Task<IActionResult> RegisterExport([FromQuery] ReimbursementClaimQuery query, CancellationToken ct)
    {
        query.Page = 1; query.PageSize = 500; var result = await service.GetRegisterAsync(query, ct); if (!result.Succeeded) return result.ToActionResult().Result!;
        var csv = new CsvBuilder("Claim Number", "Employee Code", "Employee Name", "Claim Date", "Claimed", "Eligible", "Approved", "Taxable", "NonTaxable", "Settled", "Outstanding", "Status", "Settlement Method");
        foreach (var row in result.Value!.Items) csv.AppendRow(row.ClaimNumber, row.EmployeeCode, row.EmployeeName, row.ClaimDate.ToString("yyyy-MM-dd"), row.ClaimedAmount.ToString(CultureInfo.InvariantCulture), row.EligibleAmount.ToString(CultureInfo.InvariantCulture), row.ApprovedAmount.ToString(CultureInfo.InvariantCulture), row.TaxableAmount.ToString(CultureInfo.InvariantCulture), row.NonTaxableAmount.ToString(CultureInfo.InvariantCulture), row.SettledAmount.ToString(CultureInfo.InvariantCulture), row.OutstandingAmount.ToString(CultureInfo.InvariantCulture), row.Status.ToString(), row.SettlementMethod.ToString());
        return File(csv.ToUtf8Bytes(), "text/csv; charset=utf-8", "reimbursement-register.csv");
    }

    [HttpGet("/api/me/reimbursement-categories"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MyCategories(CancellationToken ct) => (await service.GetCategoriesAsync(ct)).ToActionResult().Result!;
    [HttpGet("/api/me/reimbursements"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MyClaims([FromQuery] ReimbursementClaimQuery query, CancellationToken ct) { var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!; query.EmployeeId = identity.Value!.EmployeeId; return (await service.GetClaimsAsync(query, ct)).ToActionResult().Result!; }
    [HttpGet("/api/me/reimbursements/{id:guid}"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MyClaim(Guid id, CancellationToken ct) { var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!; var result = await service.GetClaimsAsync(new ReimbursementClaimQuery { EmployeeId = identity.Value!.EmployeeId, PageSize = 500 }, ct); if (!result.Succeeded) return result.ToActionResult().Result!; var claim = result.Value!.Items.SingleOrDefault(x => x.Id == id); return claim is null ? NotFound() : Ok(ApiResponse<ReimbursementClaimDto>.Ok(claim)); }
    [HttpPost("/api/me/reimbursements"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MyCreate(ReimbursementClaimRequest request, CancellationToken ct) { var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!; request.EmployeeId = identity.Value!.EmployeeId; return (await service.CreateClaimAsync(request, ct)).ToActionResult().Result!; }
    [HttpPut("/api/me/reimbursements/{id:guid}"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MyUpdate(Guid id, ReimbursementClaimRequest request, CancellationToken ct) { var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!; request.EmployeeId = identity.Value!.EmployeeId; var existing = await service.GetClaimAsync(id, ct); if (!existing.Succeeded || existing.Value!.EmployeeId != request.EmployeeId) return NotFound(); return (await service.UpdateClaimAsync(id, request, ct)).ToActionResult().Result!; }
    [HttpPost("/api/me/reimbursements/{id:guid}/submit"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MySubmit(Guid id, CancellationToken ct)
    {
        var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!;
        var claim = await service.GetClaimAsync(id, ct); if (!claim.Succeeded || claim.Value!.EmployeeId != identity.Value!.EmployeeId) return NotFound();
        return (await service.SubmitAsync(id, ct)).ToActionResult().Result!;
    }
    [HttpPost("/api/me/reimbursements/{id:guid}/attachments"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MyAttachment(Guid id, ReimbursementAttachmentRequest request, CancellationToken ct)
    {
        var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!;
        var claim = await service.GetClaimAsync(id, ct); if (!claim.Succeeded || claim.Value!.EmployeeId != identity.Value!.EmployeeId) return NotFound();
        return (await service.AddAttachmentAsync(id, request, ct)).ToActionResult().Result!;
    }
    [HttpGet("/api/me/reimbursements/{id:guid}/history"), HasPermission(Permissions.Payroll.ReimbursementsRequest)] public async Task<IActionResult> MyHistory(Guid id, CancellationToken ct) { var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!; var claim = await service.GetClaimAsync(id, ct); if (!claim.Succeeded || claim.Value!.EmployeeId != identity.Value!.EmployeeId) return NotFound(); return (await service.GetHistoryAsync(id, ct)).ToActionResult().Result!; }

    private async Task<Result<AccountEmployeeCurrentDto>> LinkedEmployeeAsync(CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Result<AccountEmployeeCurrentDto>.Unauthorized("No authenticated user.");
        var state = await links.GetUserAsync(userId, ct);
        if (!state.Succeeded) return Result<AccountEmployeeCurrentDto>.Failure(state.Status, state.Message, state.Errors);
        return state.Value?.CurrentLink is null ? Result<AccountEmployeeCurrentDto>.Unauthorized("No linked employee identity.") : Result<AccountEmployeeCurrentDto>.Success(state.Value.CurrentLink);
    }
}
