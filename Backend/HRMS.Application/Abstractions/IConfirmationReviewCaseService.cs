using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Phase 9c-2 confirmation review case lifecycle: create/initialize a case (with snapshotted questions and
/// resolved reviewers), collect Reporting-Manager and HR/HRBP reviews (draft + submit), and expose the audit
/// history. This service collects reviews only — it makes no confirm/extend/reject decision and changes no
/// employee status (deferred to Phase 9c-3).
/// </summary>
public interface IConfirmationReviewCaseService
{
    Task<Result<PagedResult<ConfirmationReviewCaseDto>>> GetCasesAsync(ConfirmationReviewCaseQuery query, CancellationToken ct = default);

    Task<Result<ConfirmationReviewCaseDetailDto>> GetCaseAsync(Guid caseId, CancellationToken ct = default);

    /// <summary>Creates the review case for an employee's current probation cycle, or returns the existing one (idempotent).</summary>
    Task<Result<ConfirmationReviewCaseDetailDto>> CreateCaseAsync(CreateConfirmationReviewCaseRequest request, CancellationToken ct = default);

    Task<Result<ConfirmationReviewerFormDto>> GetReviewerFormAsync(Guid caseId, ConfirmationReviewerType reviewerType, CancellationToken ct = default);

    Task<Result<ConfirmationReviewerReviewDto>> SaveDraftAsync(Guid caseId, ConfirmationReviewerType reviewerType, SaveReviewerDraftRequest request, CancellationToken ct = default);

    Task<Result<ConfirmationReviewerReviewDto>> SubmitReviewAsync(Guid caseId, ConfirmationReviewerType reviewerType, SubmitReviewerReviewRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ConfirmationReviewCaseEventDto>>> GetHistoryAsync(Guid caseId, CancellationToken ct = default);
}
