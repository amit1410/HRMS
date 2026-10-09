using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// Append-only audit of one decision <em>calculation</em> (a preview) for a review case (Phase 9c-3). Every time
/// the decision engine scores a case — whether a dry-run preview or the calculation that backed a submitted
/// decision — a row is written here so the calculated overall rating, the matched rule and the resulting outcome
/// are reproducible for audit. The calculation always uses the case's immutable question/rating snapshot, never
/// the current master configuration.
/// </summary>
public sealed class ConfirmationDecisionCalculation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewCaseId { get; set; }

    /// <summary>Review round number, defaults to 1. Incremented on each Additional Review.</summary>
    public int ReviewRound { get; set; } = 1;

    /// <summary>The overall rating the engine computed (null when no Rating answers were available).</summary>
    public decimal? OverallRating { get; set; }

    /// <summary>The decision rule that matched the overall rating (null when no rule matched).</summary>
    public Guid? AppliedRuleId { get; set; }

    /// <summary>The outcome the engine calculated (Pending when nothing matched).</summary>
    public ConfirmationOutcome CalculatedOutcome { get; set; }

    /// <summary>Human-readable / JSON explanation of how the outcome was reached.</summary>
    public string? Explanation { get; set; }

    public Guid? ActorUserId { get; set; }
    public DateTime CalculatedAtUtc { get; set; }

    public ConfirmationReviewCase? ConfirmationReviewCase { get; set; }
}

/// <summary>
/// Append-only record of a <em>final</em> decision applied to a review case (Phase 9c-3). Captures the outcome,
/// the reason/comments, the decision maker, the timestamp, the calculated rating and the rule used, and the
/// case status before/after — so the full decision provenance is never lost. A terminal outcome is applied at
/// most once per cycle (enforced by a compare-and-swap on the case status); an <c>AdditionalReview</c> outcome
/// is also recorded here but does not close the case.
/// </summary>
public sealed class ConfirmationDecision : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewCaseId { get; set; }

    /// <summary>Identity of the probation cycle this decision was made for (matches the case's cycle key).</summary>
    public string ProbationCycleKey { get; set; } = string.Empty;

    /// <summary>Review round number, defaults to 1. Incremented on each Additional Review.</summary>
    public int ReviewRound { get; set; } = 1;

    public ConfirmationOutcome Outcome { get; set; }
    public string? DecisionReason { get; set; }

    public Guid? DecisionMakerUserId { get; set; }
    public Guid? DecisionMakerEmployeeId { get; set; }
    public DateTime DecidedAtUtc { get; set; }

    public decimal? CalculatedOverallRating { get; set; }
    public Guid? AppliedRuleId { get; set; }

    public ConfirmationReviewCaseStatus PreviousCaseStatus { get; set; }
    public ConfirmationReviewCaseStatus NewCaseStatus { get; set; }

    public ConfirmationReviewCase? ConfirmationReviewCase { get; set; }
}

/// <summary>
/// A persisted probation cycle for an employee (Phase 9c-3). Exactly one cycle is <see cref="ConfirmationProbationCycleStatus.Active"/>
/// per employee at a time. The baseline cycle (CycleNumber 1) mirrors the policy-derived probation window; a
/// probation <em>extension</em> closes the active cycle and opens a new one with a later <see cref="CurrentEndDate"/>
/// and an incremented <see cref="CycleNumber"/>. The confirmation policy resolver prefers the active cycle's
/// <see cref="CurrentEndDate"/> over the DOJ+duration baseline, which is what makes the Phase 9b scheduler
/// (whose dedup is keyed on the probation-end date) re-notify for the new cycle while never re-notifying the old
/// one. Closed cycles are retained for history.
/// </summary>
public sealed class ConfirmationProbationCycle : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    public Guid ConfirmationPolicyId { get; set; }
    public Guid ConfirmationPolicyVersionId { get; set; }

    /// <summary>1 for the baseline cycle; +1 for each extension.</summary>
    public int CycleNumber { get; set; } = 1;

    public DateOnly StartDate { get; set; }

    /// <summary>The policy-derived probation end for this cycle's starting duration (before any extension).</summary>
    public DateOnly BaselineEndDate { get; set; }

    /// <summary>The effective probation end for this cycle (equals baseline until extended within the cycle).</summary>
    public DateOnly CurrentEndDate { get; set; }

    public ConfirmationProbationCycleStatus Status { get; set; } = ConfirmationProbationCycleStatus.Active;

    /// <summary>Number of extensions applied across the chain up to and including this cycle.</summary>
    public int ExtensionCount { get; set; }

    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public ICollection<ConfirmationProbationExtension> Extensions { get; set; } = new List<ConfirmationProbationExtension>();
}

/// <summary>
/// Append-only history of one probation extension (Phase 9c-3). Records the previous end date, the extension
/// duration/unit, the resulting new end date, the decision maker, the reason and the timestamp. Linked to the
/// cycle it created and to the case whose decision triggered it. Historical probation periods are never
/// overwritten — each extension adds a row (and a new <see cref="ConfirmationProbationCycle"/>).
/// </summary>
public sealed class ConfirmationProbationExtension : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The new cycle created by this extension.</summary>
    public Guid ConfirmationProbationCycleId { get; set; }

    /// <summary>The review case whose decision triggered the extension.</summary>
    public Guid ConfirmationReviewCaseId { get; set; }

    public DateOnly PreviousEndDate { get; set; }
    public int ExtensionDuration { get; set; }
    public ProbationDurationUnit ExtensionDurationUnit { get; set; } = ProbationDurationUnit.Months;
    public DateOnly NewEndDate { get; set; }

    public Guid? DecisionMakerUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime ExtendedAtUtc { get; set; }

    public ConfirmationProbationCycle? ConfirmationProbationCycle { get; set; }
}
