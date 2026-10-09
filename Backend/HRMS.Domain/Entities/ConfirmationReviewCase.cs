using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A Phase 9c-2 confirmation review case for one employee's probation cycle. The case is the unit that
/// turns configured 9c-1 review questions into an actual review: at creation the applicable questions are
/// <em>snapshotted</em> into <see cref="Questions"/> (text, type, mandatory, ordering, options, rating
/// scale) so a later change to the master question can never alter an already-created case. The Reporting
/// Manager and HR/HRBP each own a <see cref="ConfirmationReviewerReview"/>; answers live on the review,
/// never on the employee record.
/// <para>
/// This entity is deliberately independent of <see cref="ConfirmationReminderDelivery"/> (Phase 9b) and
/// carries <em>no</em> confirm/extend/reject decision, rating outcome or status change — those belong to
/// Phase 9c-3. <see cref="ProbationCycleKey"/> identifies the cycle and, together with the employee, is
/// unique so the creation service is safe to call repeatedly.
/// </para>
/// </summary>
public sealed class ConfirmationReviewCase : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    public Guid ConfirmationPolicyId { get; set; }
    public Guid ConfirmationPolicyVersionId { get; set; }

    /// <summary>
    /// Stable identifier of the probation cycle this case covers (probation start+end for the resolved
    /// policy). Unique per employee so re-running creation returns the existing case rather than duplicating.
    /// </summary>
    public string ProbationCycleKey { get; set; } = string.Empty;

    /// <summary>Review round number, defaults to 1. Incremented on each Additional Review.</summary>
    public int ReviewRound { get; set; } = 1;

    public DateOnly ProbationStartDate { get; set; }
    public DateOnly ProbationEndDate { get; set; }

    public ConfirmationReviewCaseStatus Status { get; set; } = ConfirmationReviewCaseStatus.Open;

    /// <summary>
    /// The decision outcome for this case (Phase 9c-3). <see cref="ConfirmationOutcome.Pending"/> until a final
    /// decision is applied. A terminal outcome (Confirmed / ProbationExtended / NotConfirmed) accompanies
    /// <see cref="ConfirmationReviewCaseStatus.Decided"/>; AdditionalReview keeps the case active.
    /// </summary>
    public ConfirmationOutcome Outcome { get; set; } = ConfirmationOutcome.Pending;

    /// <summary>1 for the first probation cycle; incremented when a case is raised for an extended cycle.</summary>
    public int CycleNumber { get; set; } = 1;

    /// <summary>When the terminal decision was applied (null until decided).</summary>
    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>The platform user who applied the terminal decision (null until decided).</summary>
    public Guid? DecisionMakerUserId { get; set; }

    /// <summary>When the reviews are due (probation end minus the policy's trigger-days-before-end).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Resolved Reporting Manager/L1 reviewer (null when none is assigned on the effective date).</summary>
    public Guid? ManagerReviewerEmployeeId { get; set; }

    /// <summary>Resolved HR/HRBP reviewer (null when none could be derived or the match was ambiguous).</summary>
    public Guid? HrbpReviewerEmployeeId { get; set; }

    public string? CreatedBy { get; set; }

    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public ConfirmationPolicy? ConfirmationPolicy { get; set; }
    public Employee? ManagerReviewer { get; set; }
    public Employee? HrbpReviewer { get; set; }
    public ICollection<ConfirmationReviewCaseQuestion> Questions { get; set; } = new List<ConfirmationReviewCaseQuestion>();
    public ICollection<ConfirmationReviewerReview> Reviews { get; set; } = new List<ConfirmationReviewerReview>();
    public ICollection<ConfirmationReviewCaseEvent> Events { get; set; } = new List<ConfirmationReviewCaseEvent>();
    public ICollection<ConfirmationDecisionCalculation> Calculations { get; set; } = new List<ConfirmationDecisionCalculation>();
    public ICollection<ConfirmationDecision> Decisions { get; set; } = new List<ConfirmationDecision>();
}

/// <summary>
/// A point-in-time snapshot of one applicable review question, copied onto the case at creation. All display
/// data is denormalized here (<see cref="QuestionText"/>, <see cref="QuestionType"/>, rating bounds) so the
/// historical review renders and validates identically forever, regardless of later master edits.
/// <see cref="SourceQuestionId"/> is kept only for traceability and is never used to re-read the master.
/// </summary>
public sealed class ConfirmationReviewCaseQuestion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewCaseId { get; set; }

    /// <summary>The master question this was snapshotted from (traceability only).</summary>
    public Guid SourceQuestionId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public ReviewQuestionType QuestionType { get; set; }
    public bool IsMandatory { get; set; }
    public int DisplayOrder { get; set; }

    // Snapshot of the rating scale (populated only for Rating questions).
    public Guid? SourceRatingScaleId { get; set; }
    public string? RatingScaleCode { get; set; }
    public string? RatingScaleName { get; set; }
    public int? RatingMin { get; set; }
    public int? RatingMax { get; set; }

    public ConfirmationReviewCase? ConfirmationReviewCase { get; set; }
    public ICollection<ConfirmationReviewCaseQuestionOption> Options { get; set; } = new List<ConfirmationReviewCaseQuestionOption>();
    public ICollection<ConfirmationReviewCaseRatingValue> RatingValues { get; set; } = new List<ConfirmationReviewCaseRatingValue>();
}

/// <summary>Snapshot of one Dropdown/MultiSelect option for a <see cref="ConfirmationReviewCaseQuestion"/>.</summary>
public sealed class ConfirmationReviewCaseQuestionOption : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewCaseQuestionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public ConfirmationReviewCaseQuestion? ConfirmationReviewCaseQuestion { get; set; }
}

/// <summary>Snapshot of one point on the rating scale for a Rating <see cref="ConfirmationReviewCaseQuestion"/>.</summary>
public sealed class ConfirmationReviewCaseRatingValue : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewCaseQuestionId { get; set; }
    public int RatingValue { get; set; }
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public ConfirmationReviewCaseQuestion? ConfirmationReviewCaseQuestion { get; set; }
}

/// <summary>
/// One reviewer's review of a case — a Reporting-Manager or HR/HRBP review. A review is created as a
/// <see cref="ConfirmationReviewStatus.Draft"/> that can be edited; on submission it becomes
/// <see cref="ConfirmationReviewStatus.Submitted"/> and is thereafter immutable. A correction to a submitted
/// review creates a new <see cref="VersionNumber"/> and marks the prior version
/// <see cref="ConfirmationReviewStatus.Superseded"/>, so the submitted record is never silently changed.
/// </summary>
public sealed class ConfirmationReviewerReview : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewCaseId { get; set; }
    public int ReviewRound { get; set; } = 1;

    public ConfirmationReviewerType ReviewerType { get; set; }

    /// <summary>The employee acting as reviewer (resolved at submission, null while an unassigned draft).</summary>
    public Guid? ReviewerEmployeeId { get; set; }

    public int VersionNumber { get; set; } = 1;
    public ConfirmationReviewStatus Status { get; set; } = ConfirmationReviewStatus.Draft;
    public string? OverallComments { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public string? SubmittedBy { get; set; }

    public ConfirmationReviewCase? ConfirmationReviewCase { get; set; }
    public ICollection<ConfirmationReviewResponse> Responses { get; set; } = new List<ConfirmationReviewResponse>();
}

/// <summary>
/// One reviewer answer to one snapshotted question. The shape used depends on the snapshot question type:
/// <see cref="RatingValue"/> for Rating, <see cref="BoolValue"/> for Yes/No, <see cref="TextValue"/> for
/// Text, and <see cref="SelectedOptions"/> for Dropdown (0..1) / MultiSelect (0..n). Answers are validated
/// against the <em>snapshot</em>, never the live master.
/// </summary>
public sealed class ConfirmationReviewResponse : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewerReviewId { get; set; }
    public Guid ConfirmationReviewCaseQuestionId { get; set; }

    public int? RatingValue { get; set; }
    public bool? BoolValue { get; set; }
    public string? TextValue { get; set; }

    public ConfirmationReviewerReview? ConfirmationReviewerReview { get; set; }
    public ConfirmationReviewCaseQuestion? ConfirmationReviewCaseQuestion { get; set; }
    public ICollection<ConfirmationReviewResponseOption> SelectedOptions { get; set; } = new List<ConfirmationReviewResponseOption>();
}

/// <summary>One selected option code for a Dropdown/MultiSelect <see cref="ConfirmationReviewResponse"/>.</summary>
public sealed class ConfirmationReviewResponseOption : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewResponseId { get; set; }

    /// <summary>The snapshot option code chosen (validated against the snapshot question's options).</summary>
    public string OptionCode { get; set; } = string.Empty;

    public ConfirmationReviewResponse? ConfirmationReviewResponse { get; set; }
}

/// <summary>
/// Append-only audit entry for a confirmation review case: creation, question snapshotting, reviewer
/// resolution, draft saves and submissions/supersessions. Historical submitted review data is never
/// overwritten; every change is recorded here instead.
/// </summary>
public sealed class ConfirmationReviewCaseEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationReviewCaseId { get; set; }

    /// <summary>The reviewer review this event concerns, when applicable.</summary>
    public Guid? ConfirmationReviewerReviewId { get; set; }

    public ConfirmationReviewCaseEventType EventType { get; set; }
    public Guid? ActorUserId { get; set; }

    /// <summary>Human/JSON detail for the event (e.g. snapshot counts, ambiguity reason, version numbers).</summary>
    public string? Details { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public ConfirmationReviewCase? ConfirmationReviewCase { get; set; }
}
