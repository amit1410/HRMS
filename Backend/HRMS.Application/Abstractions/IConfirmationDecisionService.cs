using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Phase 9c-3 confirmation decision engine. Scores a completed review case against its immutable
/// question/rating snapshot and the policy version's configured decision rules, then — once an authorized
/// decider submits — applies the terminal outcome (confirm / extend probation / not-confirmed / additional
/// review) with concurrency protection and full audit. It never mutates employee data until a final decision
/// is submitted, and it does not re-open or re-decide a case that has already been decided.
/// </summary>
public interface IConfirmationDecisionService
{
    /// <summary>
    /// A dry-run evaluation of the case: readiness, calculated overall rating, matched rule, calculated outcome
    /// and permitted outcomes. Appends a calculation row for audit. Changes no employee data.
    /// </summary>
    Task<Result<ConfirmationDecisionPreviewDto>> CalculateAsync(Guid caseId, CancellationToken ct = default, int? reviewRound = null);

    /// <summary>
    /// Apply a final decision to the case. Validates readiness, authorization and that the submitted outcome is
    /// permitted, then atomically transitions the case and applies the employee-side effects.
    /// </summary>
    Task<Result<ConfirmationDecisionResultDto>> SubmitDecisionAsync(Guid caseId, SubmitConfirmationDecisionRequest request, CancellationToken ct = default, int? reviewRound = null);

    /// <summary>The append-only decision provenance for a case (final decisions, calculations, extensions).</summary>
    Task<Result<ConfirmationDecisionHistoryDto>> GetHistoryAsync(Guid caseId, CancellationToken ct = default);
}
