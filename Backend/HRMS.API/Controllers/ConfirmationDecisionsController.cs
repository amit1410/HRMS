using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Phase 9c-3 confirmation decision workflow for a review case: preview the calculated outcome, submit the final
/// decision (confirm / extend / not-confirmed / additional review) and read the decision history. Decision actions
/// require <see cref="Permissions.Confirmation.Decide"/>; the history is readable with case-view permission.
/// </summary>
[ApiController, Route("api/confirmation-review-cases/{caseId:guid}/decision"), Produces("application/json")]
public sealed class ConfirmationDecisionsController : ControllerBase
{
    private readonly IConfirmationDecisionService _service;
    public ConfirmationDecisionsController(IConfirmationDecisionService service) => _service = service;

    /// <summary>Dry-run the decision engine (records a calculation for audit). Does not change employee data.</summary>
    [HttpPost("preview"), HasPermission(Permissions.Confirmation.Decide)]
    public async Task<ActionResult<ApiResponse<ConfirmationDecisionPreviewDto>>> Preview(Guid caseId, [FromQuery] int? reviewRound, CancellationToken ct) =>
        (await _service.CalculateAsync(caseId, ct, reviewRound)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Confirmation.Decide)]
    public async Task<ActionResult<ApiResponse<ConfirmationDecisionResultDto>>> Submit(Guid caseId, [FromQuery] int? reviewRound, [FromBody] SubmitConfirmationDecisionRequest request, CancellationToken ct) =>
        (await _service.SubmitDecisionAsync(caseId, request, ct, reviewRound)).ToActionResult();

    [HttpGet("history"), HasPermission(Permissions.Confirmation.ReviewCaseView)]
    public async Task<ActionResult<ApiResponse<ConfirmationDecisionHistoryDto>>> History(Guid caseId, CancellationToken ct) =>
        (await _service.GetHistoryAsync(caseId, ct)).ToActionResult();
}
