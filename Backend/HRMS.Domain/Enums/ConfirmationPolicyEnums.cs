namespace HRMS.Domain.Enums;

/// <summary>Lifecycle of a confirmation/probation policy version. Mirrors <see cref="LeavePolicyVersionStatus"/>.</summary>
public enum ConfirmationPolicyVersionStatus { Draft = 0, Published = 1, Retired = 2 }

/// <summary>Unit for a configured probation or extension duration.</summary>
public enum ProbationDurationUnit { Months = 0, Days = 1 }

/// <summary>
/// Who must approve a confirmation decision before it is applied. In Phase 9c-3 an "approval" is a submitted
/// reviewer review (there is no separate approval artefact): this requirement decides <em>which</em> reviewer
/// reviews must be submitted before a case becomes <c>Completed</c> / a decision may be made.
/// <see cref="ManagerOrHrbp"/> needs either; <see cref="ManagerAndHrbp"/> needs both.
/// </summary>
public enum ConfirmationApprovalRequirement { Manager = 0, Hrbp = 1, ManagerAndHrbp = 2, ManagerOrHrbp = 3 }

/// <summary>
/// Ordering constraint applied to reviewer-review submission. <see cref="Parallel"/> lets Manager and HRBP
/// submit in any order; <see cref="ManagerThenHrbp"/> blocks the HRBP review until the Manager review is
/// submitted. Only meaningful when both reviews are required.
/// </summary>
public enum ApprovalSequence { Parallel = 0, ManagerThenHrbp = 1 }

/// <summary>
/// How the single overall rating for a case is computed from the reviewer's answers to the snapshotted
/// Rating questions. Configured per policy so the calculation is never hard-coded.
/// </summary>
public enum OverallRatingMethod { AverageOfRatings = 0, MinimumRating = 1 }

/// <summary>Lifecycle of a persisted probation cycle. Exactly one cycle is <see cref="Active"/> per employee.</summary>
public enum ConfirmationProbationCycleStatus { Active = 0, Closed = 1 }

/// <summary>The outcome of a confirmation review decision (Phase 9c-3).</summary>
public enum ConfirmationOutcome { Pending = 0, Confirmed = 1, ProbationExtended = 2, NotConfirmed = 3, AdditionalReview = 4 }
