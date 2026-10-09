using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/confirmation-review-cases"), Produces("application/json")]
public sealed class ConfirmationReviewCasesController : ControllerBase
{
    private readonly IConfirmationReviewCaseService _service;
    public ConfirmationReviewCasesController(IConfirmationReviewCaseService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Confirmation.ReviewCaseView)]
    public async Task<ActionResult<ApiResponse<PagedResult<ConfirmationReviewCaseDto>>>> GetAll([FromQuery] ConfirmationReviewCaseQuery query, CancellationToken ct) =>
        (await _service.GetCasesAsync(query, ct)).ToActionResult();

    [HttpGet("{caseId:guid}", Name = nameof(GetConfirmationReviewCase)), HasPermission(Permissions.Confirmation.ReviewCaseView)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewCaseDetailDto>>> GetConfirmationReviewCase(Guid caseId, CancellationToken ct) =>
        (await _service.GetCaseAsync(caseId, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Confirmation.ReviewCaseManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewCaseDetailDto>>> Create([FromBody] CreateConfirmationReviewCaseRequest request, CancellationToken ct) =>
        (await _service.CreateCaseAsync(request, ct)).ToCreatedResult(nameof(GetConfirmationReviewCase), x => new { caseId = x.Case.Id });

    [HttpGet("{caseId:guid}/reviewer-form"), HasPermission(Permissions.Confirmation.ReviewCaseView)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewerFormDto>>> GetReviewerForm(Guid caseId, [FromQuery] ConfirmationReviewerType reviewerType, CancellationToken ct) =>
        (await _service.GetReviewerFormAsync(caseId, reviewerType, ct)).ToActionResult();

    [HttpPut("{caseId:guid}/reviews/manager/draft"), HasPermission(Permissions.Confirmation.ReviewManagerSubmit)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewerReviewDto>>> SaveManagerDraft(Guid caseId, [FromBody] SaveReviewerDraftRequest request, CancellationToken ct) =>
        (await _service.SaveDraftAsync(caseId, ConfirmationReviewerType.ReportingManager, request, ct)).ToActionResult();

    [HttpPost("{caseId:guid}/reviews/manager/submit"), HasPermission(Permissions.Confirmation.ReviewManagerSubmit)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewerReviewDto>>> SubmitManagerReview(Guid caseId, [FromBody] SubmitReviewerReviewRequest request, CancellationToken ct) =>
        (await _service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, request, ct)).ToActionResult();

    [HttpPut("{caseId:guid}/reviews/hrbp/draft"), HasPermission(Permissions.Confirmation.ReviewHrbpSubmit)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewerReviewDto>>> SaveHrbpDraft(Guid caseId, [FromBody] SaveReviewerDraftRequest request, CancellationToken ct) =>
        (await _service.SaveDraftAsync(caseId, ConfirmationReviewerType.Hrbp, request, ct)).ToActionResult();

    [HttpPost("{caseId:guid}/reviews/hrbp/submit"), HasPermission(Permissions.Confirmation.ReviewHrbpSubmit)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewerReviewDto>>> SubmitHrbpReview(Guid caseId, [FromBody] SubmitReviewerReviewRequest request, CancellationToken ct) =>
        (await _service.SubmitReviewAsync(caseId, ConfirmationReviewerType.Hrbp, request, ct)).ToActionResult();

    [HttpGet("{caseId:guid}/history"), HasPermission(Permissions.Confirmation.ReviewCaseView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ConfirmationReviewCaseEventDto>>>> GetHistory(Guid caseId, CancellationToken ct) =>
        (await _service.GetHistoryAsync(caseId, ct)).ToActionResult();
}
