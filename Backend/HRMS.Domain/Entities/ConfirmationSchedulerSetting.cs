using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// Per-tenant configuration for the Phase 9b confirmation/probation reminder scheduler. Exactly one row per
/// tenant; absence means "use the global <c>ConfirmationReminders</c> defaults with the tenant enabled".
/// <para>
/// The global worker options set the master switch and the host tick interval; this row lets each tenant/
/// customer tune its own cadence and delivery policy without a redeploy. Overrides that are null fall back
/// to the global option value. None of these settings change an employee's Confirmation Date or Job Status —
/// that is the Phase 9c decision workflow.
/// </para>
/// </summary>
public sealed class ConfirmationSchedulerSetting : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Whether the scheduler scans this tenant at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often (minutes) this tenant is scanned. The host worker ticks more finely; a tenant is
    /// only processed once this much time has elapsed since <see cref="LastRunAtUtc"/>.</summary>
    public int FrequencyMinutes { get; set; } = 60;

    /// <summary>Fallback trigger window used only when the applicable policy rule leaves it at zero. The
    /// policy's own configured trigger window is authoritative when set.</summary>
    public int DefaultTriggerDaysBeforeEnd { get; set; } = 30;

    /// <summary>Override for the global max delivery attempts before a recipient is abandoned. Null = global.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>Override for the claim lease length in minutes. Null = global.</summary>
    public int? ClaimLeaseMinutes { get; set; }

    /// <summary>When false (default), a recipient that was successfully notified for a probation cycle is never
    /// notified again for the same cycle. When true, a completed occurrence may be re-raised.</summary>
    public bool ResendProcessed { get; set; }

    public bool NotifyEmployee { get; set; } = true;
    public bool NotifyManager { get; set; } = true;
    public bool NotifyHrbp { get; set; } = true;

    /// <summary>Last time the scheduler processed this tenant; drives the frequency gate.</summary>
    public DateTime? LastRunAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public Tenant? Tenant { get; set; }
}
