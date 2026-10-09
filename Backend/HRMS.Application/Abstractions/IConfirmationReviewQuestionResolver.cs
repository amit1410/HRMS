using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public enum ConfirmationReviewQuestionResolutionStatus
{
    Resolved,
    InvalidTenant,
    EffectiveEmploymentNotFound,
    EffectiveEmploymentAmbiguous
}

/// <summary>One applicable, fully-hydrated review question ready to be snapshotted onto a review case.</summary>
public sealed record ResolvedReviewQuestion(
    Guid QuestionId,
    string Code,
    string QuestionText,
    string? HelpText,
    ReviewQuestionType QuestionType,
    bool IsMandatory,
    int DisplayOrder,
    Guid? RatingScaleId,
    string? RatingScaleCode,
    string? RatingScaleName,
    int? RatingMin,
    int? RatingMax,
    IReadOnlyList<ResolvedReviewQuestionOption> Options,
    IReadOnlyList<ResolvedReviewRatingValue> RatingValues);

public sealed record ResolvedReviewQuestionOption(string Code, string Label, int DisplayOrder);

public sealed record ResolvedReviewRatingValue(int RatingValue, string Label, int DisplayOrder);

public sealed record ConfirmationReviewQuestionResolutionResult(
    ConfirmationReviewQuestionResolutionStatus Status,
    IReadOnlyList<ResolvedReviewQuestion> Questions,
    string Message);

/// <summary>
/// Resolves the set of configured 9c-1 review questions that apply to an employee on a date, reusing the
/// confirmation-policy applicability match/specificity philosophy (Phase 9a). The result is a deterministic,
/// ordered list the case service snapshots verbatim.
/// </summary>
public interface IConfirmationReviewQuestionResolver
{
    Task<ConfirmationReviewQuestionResolutionResult> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly effectiveDate,
        Guid? resolvedPolicyId,
        CancellationToken ct = default);
}
