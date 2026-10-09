namespace HRMS.Domain.Enums;

/// <summary>
/// The answer shape of a confirmation review question. Tenant-configured per question so review forms
/// (Phase 9c-2) are rendered from data, never hard-coded. <see cref="Rating"/> questions reference a
/// configured <c>ConfirmationRatingScale</c>; <see cref="Dropdown"/>/<see cref="MultiSelect"/> questions
/// carry configured options.
/// </summary>
public enum ReviewQuestionType
{
    Rating = 0,
    YesNo = 1,
    Text = 2,
    Dropdown = 3,
    MultiSelect = 4
}

/// <summary>
/// Lifecycle of a confirmation review case. <see cref="Completed"/> means the <em>required</em> reviewer
/// reviews (per the policy's approval requirement) have been submitted — the case is ready for a decision.
/// <see cref="Decided"/> is the terminal state reached when the Phase 9c-3 decision workflow applies a
/// Confirm / Extend / Not-Confirmed outcome (the specific outcome is stored on the case and in the decision
/// history). An <c>AdditionalReview</c> outcome deliberately does NOT move the case here — it returns the
/// case to <see cref="InReview"/> so another review cycle can run. <see cref="Cancelled"/> is reserved for
/// future supersession.
/// </summary>
public enum ConfirmationReviewCaseStatus
{
    Open = 0,
    InReview = 1,
    Completed = 2,
    Cancelled = 3,
    Decided = 4
}

/// <summary>Which reviewer a <c>ConfirmationReviewerReview</c> belongs to.</summary>
public enum ConfirmationReviewerType
{
    ReportingManager = 0,
    Hrbp = 1
}

/// <summary>
/// State of a single reviewer review version. A <see cref="Draft"/> may be edited in place; once
/// <see cref="Submitted"/> it is never mutated — a correction creates a new version and the prior one is
/// marked <see cref="Superseded"/> so the submitted history is preserved for audit.
/// </summary>
public enum ConfirmationReviewStatus
{
    Draft = 0,
    Submitted = 1,
    Superseded = 2
}

/// <summary>
/// Append-only audit event recorded against a confirmation review case. Values 0–5 are the Phase 9c-2
/// case/review lifecycle events; values 6+ are the Phase 9c-3 decision-workflow events. Never delete or
/// renumber existing values — they are persisted.
/// </summary>
public enum ConfirmationReviewCaseEventType
{
    CaseCreated = 0,
    QuestionsSnapshotted = 1,
    ReviewersResolved = 2,
    DraftSaved = 3,
    ReviewSubmitted = 4,
    ReviewSuperseded = 5,

    // Phase 9c-3 decision workflow.
    DecisionCalculated = 6,
    DecisionSubmitted = 7,
    EmployeeConfirmed = 8,
    ProbationExtended = 9,
    MarkedNotConfirmed = 10,
    AdditionalReviewRequested = 11,
    ProbationCycleCreated = 12
}
