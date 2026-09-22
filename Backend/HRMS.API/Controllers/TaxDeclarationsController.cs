using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/me/tax-declarations")]
public sealed class MyTaxDeclarationsController(ITaxDeclarationService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.TaxDeclarationViewOwn)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> Get(CancellationToken ct) => (await service.GetOwnAsync(ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> Create(Guid cycleId, CancellationToken ct) => (await service.CreateOwnAsync(cycleId, ct)).ToCreatedResult(nameof(Get), _ => new { });
    [HttpPost("lines"), HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> AddLine(TaxDeclarationLineRequest request, CancellationToken ct) => (await service.AddLineOwnAsync(request, ct)).ToActionResult();
    [HttpPut("{declarationId:guid}/lines/{lineId:guid}"), HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> UpdateLine(Guid declarationId, Guid lineId, TaxDeclarationLineUpdateRequest request, CancellationToken ct) => (await service.UpdateLineOwnAsync(declarationId, lineId, request, ct)).ToActionResult();
    [HttpDelete("{declarationId:guid}/lines/{lineId:guid}"), HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> DeleteLine(Guid declarationId, Guid lineId, CancellationToken ct) => (await service.DeleteLineOwnAsync(declarationId, lineId, ct)).ToActionResult();
    [HttpPost("submit"), HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> Submit(CancellationToken ct) => (await service.SubmitOwnAsync(ct)).ToActionResult();
    [HttpPost("{declarationId:guid}/resubmit"), HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> Resubmit(Guid declarationId, CancellationToken ct) => (await service.ResubmitOwnAsync(declarationId, ct)).ToActionResult();
    [HttpPost("proofs"), HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<TaxDeclarationProofDto>>> AddProof(TaxDeclarationProofRequest request, CancellationToken ct) => (await service.AddProofOwnAsync(request, ct)).ToActionResult();
    [HttpPost("{declarationId:guid}/lines/{lineId:guid}/proofs/{proofId:guid}/replace"), HasPermission(Permissions.Payroll.TaxDeclarationManageOwn)] public async Task<ActionResult<ApiResponse<TaxDeclarationProofDto>>> ReplaceProof(Guid declarationId, Guid lineId, Guid proofId, TaxDeclarationProofRequest request, CancellationToken ct) => (await service.ReplaceProofOwnAsync(declarationId, lineId, proofId, request, ct)).ToActionResult();
}

[ApiController, Route("api/payroll/tax-declarations")]
public sealed class TaxDeclarationsController(ITaxDeclarationService service) : ControllerBase
{
    [HttpGet("cycles"), HasPermission(Permissions.Payroll.TaxDeclarationReview)] public async Task<ActionResult<ApiResponse<IReadOnlyList<TaxDeclarationCycleDto>>>> Cycles(CancellationToken ct) => (await service.GetCyclesAsync(ct)).ToActionResult();
    [HttpPost("cycles"), HasPermission(Permissions.Payroll.TaxDeclarationConfigure)] public async Task<ActionResult<ApiResponse<TaxDeclarationCycleDto>>> CreateCycle(TaxDeclarationCycleRequest request, CancellationToken ct) => (await service.CreateCycleAsync(request, ct)).ToCreatedResult(nameof(Cycles), _ => new { });
    [HttpPost("categories"), HasPermission(Permissions.Payroll.TaxDeclarationConfigure)] public async Task<ActionResult<ApiResponse<TaxDeclarationCategoryDto>>> CreateCategory(TaxDeclarationCategoryRequest request, CancellationToken ct) => (await service.CreateCategoryAsync(request, ct)).ToActionResult();
    [HttpPost("items"), HasPermission(Permissions.Payroll.TaxDeclarationConfigure)] public async Task<ActionResult<ApiResponse<TaxDeclarationItemDto>>> CreateItem(TaxDeclarationItemRequest request, CancellationToken ct) => (await service.CreateItemAsync(request, ct)).ToActionResult();
    [HttpGet("review"), HasPermission(Permissions.Payroll.TaxDeclarationReview)] public async Task<ActionResult<ApiResponse<PagedResult<EmployeeTaxDeclarationDto>>>> Review([FromQuery] TaxDeclarationReviewQuery query, CancellationToken ct) => (await service.GetReviewAsync(query, ct)).ToActionResult();
    [HttpPost("{id:guid}/review"), HasPermission(Permissions.Payroll.TaxDeclarationReview)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> ReviewLine(Guid id, TaxDeclarationReviewRequest request, CancellationToken ct) => (await service.ReviewLineAsync(id, request, ct)).ToActionResult();
    [HttpPost("{id:guid}/lines/{lineId:guid}/proofs/{proofId:guid}/review"), HasPermission(Permissions.Payroll.TaxDeclarationReview)] public async Task<ActionResult<ApiResponse<TaxDeclarationProofDto>>> ReviewProof(Guid id, Guid lineId, Guid proofId, TaxDeclarationProofReviewRequest request, CancellationToken ct) => (await service.ReviewProofAsync(id, lineId, proofId, request, ct)).ToActionResult();
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Payroll.TaxDeclarationApprove)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/request-resubmission"), HasPermission(Permissions.Payroll.TaxDeclarationReview)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> RequestResubmission(Guid id, [FromBody] string? comment, CancellationToken ct) => (await service.RequestResubmissionAsync(id, comment, ct)).ToActionResult();
    [HttpPost("{id:guid}/lock"), HasPermission(Permissions.Payroll.TaxDeclarationApprove)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> Lock(Guid id, CancellationToken ct) => (await service.LockAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/reopen"), HasPermission(Permissions.Payroll.TaxDeclarationReopen)] public async Task<ActionResult<ApiResponse<EmployeeTaxDeclarationDto>>> Reopen(Guid id, [FromBody] string? reason, CancellationToken ct) => (await service.ReopenAsync(id, reason, ct)).ToActionResult();
    [HttpGet("{id:guid}/audit"), HasPermission(Permissions.Payroll.TaxDeclarationViewAudit)] public async Task<ActionResult<ApiResponse<IReadOnlyList<TaxDeclarationAuditDto>>>> Audit(Guid id, CancellationToken ct) => (await service.GetAuditAsync(id, ct)).ToActionResult();
}
