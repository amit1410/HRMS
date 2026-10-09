using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// Durable claim, delivery and execution history for one confirmation-review reminder occurrence.
/// <para>
/// Mirrors <see cref="LeaveReminderDelivery"/>: a single row is the atomic claim-and-dedup unit for one
/// (employee, probation cycle, recipient) occurrence, so two worker instances cannot send the same email
/// twice and a failed send can be retried on its own. Because each row also records the resolved policy,
/// the probation-end and trigger dates, the attempt count, the status and the created/processed timestamps,
/// the table doubles as the Phase 9b scheduler <b>execution log</b> (group by <see cref="EmployeeId"/> +
/// <see cref="ProbationEndDate"/> to see a case across its recipients).
/// </para>
/// The probation cycle is identified by <see cref="ProbationEndDate"/>, so a probation extension (which moves
/// the end date) is naturally a new occurrence and re-notifies, while the same end date never re-notifies a
/// recipient that was already <c>Sent</c>.
/// </summary>
public sealed class ConfirmationReminderDelivery : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>The employee approaching probation completion (the subject of the review).</summary>
    public Guid EmployeeId { get; set; }

    public Guid ConfirmationPolicyId { get; set; }
    public Guid ConfirmationPolicyVersionId { get; set; }

    /// <summary>Policy-derived probation end date; the identity of the probation cycle.</summary>
    public DateOnly ProbationEndDate { get; set; }

    /// <summary>Probation end minus the effective trigger window (never before Date of Joining).</summary>
    public DateOnly TriggerDate { get; set; }

    public ConfirmationReminderRecipientType RecipientType { get; set; }

    /// <summary>The employee behind the recipient, when the recipient is an employee (Employee/Manager, and
    /// an HRBP that is account-linked to an employee). Null when no employee backs the address.</summary>
    public Guid? RecipientEmployeeId { get; set; }

    /// <summary>Email the notification was addressed to; captured for the audit/execution log.</summary>
    public string? RecipientEmail { get; set; }

    /// <summary>Unique-per-tenant occurrence identity: <c>{employeeId:N}:{probationEnd}:{recipientType}</c>.</summary>
    public string OccurrenceKey { get; set; } = string.Empty;

    public string NotificationType { get; set; } = string.Empty;

    /// <summary>Lifecycle/execution status: Claimed, Sent, Skipped or Failed.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime DueAtUtc { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>Identifies the worker that currently owns the claim.</summary>
    public Guid? ClaimToken { get; set; }

    public DateTime? SentAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public Employee? RecipientEmployee { get; set; }
}
