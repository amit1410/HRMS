namespace HRMS.Application.Abstractions;

/// <summary>
/// Host-wide defaults and safety limits for the Phase 9b confirmation/probation reminder scheduler. The
/// master <see cref="Enabled"/> switch and <see cref="WorkerIntervalMinutes"/> tick belong to the worker;
/// per-tenant cadence and delivery policy live in <c>ConfirmationSchedulerSetting</c> and override the
/// relevant defaults here. Mirrors <see cref="LeaveReminderOptions"/>.
/// </summary>
public sealed class ConfirmationReminderOptions
{
    public const string SectionName = "ConfirmationReminders";

    /// <summary>Master switch. When false the worker does nothing for any tenant.</summary>
    public bool Enabled { get; init; }

    /// <summary>How often the host worker wakes to consider tenants (minutes). Per-tenant frequency gates
    /// the actual scans, so this can stay small.</summary>
    public int WorkerIntervalMinutes { get; init; } = 15;

    /// <summary>Default per-tenant scan frequency applied when a tenant has no settings row.</summary>
    public int DefaultFrequencyMinutes { get; init; } = 60;

    /// <summary>Max approaching-probation cases examined per tenant per scan.</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>Max delivery attempts for one recipient occurrence before it is abandoned.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Claim lease length (minutes); a claim older than this may be taken over by another worker.</summary>
    public int ClaimLeaseMinutes { get; init; } = 30;

    /// <summary>Backoff before a failed recipient occurrence is retried (minutes).</summary>
    public int RetryBackoffMinutes { get; init; } = 60;

    /// <summary>Fallback trigger window (days before probation end) when neither the policy rule nor the
    /// tenant setting specifies one.</summary>
    public int DefaultTriggerDaysBeforeEnd { get; init; } = 30;

    public string? Validate()
    {
        if (WorkerIntervalMinutes < 1 || DefaultFrequencyMinutes < 1 || BatchSize < 1 || MaxAttempts < 1
            || ClaimLeaseMinutes < 1 || RetryBackoffMinutes < 1 || DefaultTriggerDaysBeforeEnd < 0)
            return $"{SectionName} values must be positive ({nameof(DefaultTriggerDaysBeforeEnd)} may be zero).";
        return null;
    }
}
