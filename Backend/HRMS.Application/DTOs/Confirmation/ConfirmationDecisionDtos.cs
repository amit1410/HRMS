using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Confirmation;

/// <summary>Completion state of one reviewer's review, used to show approval/readiness on the decision screen.</summary>
public sealed record ConfirmationReviewerStatusDto(
    ConfirmationReviewerType ReviewerType,
    bool IsRequired,
    bool IsSubmitted,
    bool HasComments,
    int RatingAnswerCount);

/// <summary>
/// The decision engine's assessment of a case (Phase 9c-3). Produced by a dry-run preview and whenever a
/// decision is submitted. Carries the readiness verdict, the calculated overall rating + matched rule +
/// outcome (all from the case's immutable snapshot), and the set of outcomes the configured workflow permits
/// to be submitted. A non-empty <see cref="BlockingReasons"/> means a terminal decision cannot yet be made.
/// </summary>
public sealed record ConfirmationDecisionPreviewDto(
    Guid CaseId,
    ConfirmationReviewCaseStatus Status,
    ConfirmationOutcome CaseOutcome,
    int CycleNumber,
    int ReviewRound,
    ConfirmationApprovalRequirement RequiredApproval,
    ApprovalSequence ApprovalSequence,
    OverallRatingMethod OverallRatingMethod,
    IReadOnlyList<ConfirmationReviewerStatusDto> Reviewers,
    bool IsReadyForDecision,
    IReadOnlyList<string> BlockingReasons,
    decimal? OverallRating,
    Guid? AppliedRuleId,
    string? AppliedRuleName,
    ConfirmationOutcome CalculatedOutcome,
    string Explanation,
    IReadOnlyList<ConfirmationOutcome> PermittedOutcomes);

public sealed record SubmitConfirmationDecisionRequest(
    ConfirmationOutcome Outcome,
    string? DecisionReason = null,
    DateOnly? ConfirmationDateOverride = null);

/// <summary>The result of applying a final decision: the decision record plus the resulting case / cycle state.</summary>
public sealed record ConfirmationDecisionResultDto(
    Guid DecisionId,
    Guid CaseId,
    ConfirmationOutcome Outcome,
    ConfirmationReviewCaseStatus PreviousStatus,
    ConfirmationReviewCaseStatus NewStatus,
    decimal? CalculatedOverallRating,
    Guid? AppliedRuleId,
    DateTime DecidedAtUtc,
    // Confirm
    DateOnly? ConfirmationDate,
    string? JobStatus,
    // Extend
    Guid? NewCycleId,
    int? NewCycleNumber,
    DateOnly? NewProbationEndDate,
    Guid? NewCaseId,
    int ReviewRound,
    string Message);

public sealed record ConfirmationDecisionDto(
    Guid Id,
    string ProbationCycleKey,
    ConfirmationOutcome Outcome,
    string? DecisionReason,
    Guid? DecisionMakerUserId,
    Guid? DecisionMakerEmployeeId,
    DateTime DecidedAtUtc,
    decimal? CalculatedOverallRating,
    Guid? AppliedRuleId,
    ConfirmationReviewCaseStatus PreviousCaseStatus,
    ConfirmationReviewCaseStatus NewCaseStatus,
    int ReviewRound);

public sealed record ConfirmationDecisionCalculationDto(
    Guid Id,
    decimal? OverallRating,
    Guid? AppliedRuleId,
    ConfirmationOutcome CalculatedOutcome,
    string? Explanation,
    Guid? ActorUserId,
    DateTime CalculatedAtUtc,
    int ReviewRound);

public sealed record ConfirmationProbationExtensionDto(
    Guid Id,
    int CycleNumber,
    DateOnly PreviousEndDate,
    int ExtensionDuration,
    ProbationDurationUnit ExtensionDurationUnit,
    DateOnly NewEndDate,
    Guid? DecisionMakerUserId,
    string? Reason,
    DateTime ExtendedAtUtc);

/// <summary>The full, append-only decision provenance for a case: final decisions, calc previews and extensions.</summary>
public sealed record ConfirmationDecisionHistoryDto(
    Guid CaseId,
    IReadOnlyList<ConfirmationDecisionDto> Decisions,
    IReadOnlyList<ConfirmationDecisionCalculationDto> Calculations,
    IReadOnlyList<ConfirmationProbationExtensionDto> Extensions);
