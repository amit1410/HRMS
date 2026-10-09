using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// The Phase 9b scheduler body: for the current tenant, find employees approaching probation completion, and
/// raise a confirmation-review reminder to each configured recipient exactly once per probation cycle. The
/// unit of claim/dedup/retry is one <see cref="ConfirmationReminderDelivery"/> row per (employee, probation
/// end date, recipient), so two worker instances cannot double-send and a failed send is retried on its own.
/// <para>
/// Mirrors <see cref="LeaveApprovalReminderProcessor"/>. It is strictly notify-only: it never writes an
/// employee's Confirmation Date or Job Status (that is the Phase 9c decision workflow), and it respects
/// inactive/terminated and already-confirmed employees.
/// </para>
/// </summary>
public sealed class ConfirmationReminderProcessor : IConfirmationReminderProcessor
{
    private const string NotificationType = "ConfirmationReview";
    private const string Claimed = "Claimed";
    private const string Sent = "Sent";
    private const string Skipped = "Skipped";
    private const string Failed = "Failed";

    private readonly IHrmsDbContext _db;
    private readonly IConfirmationPolicyResolver _resolver;
    private readonly IConfirmationReminderNotificationService _notifications;
    private readonly ConfirmationReminderOptions _options;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;
    private readonly ILogger<ConfirmationReminderProcessor> _logger;

    public ConfirmationReminderProcessor(
        IHrmsDbContext db,
        IConfirmationPolicyResolver resolver,
        IConfirmationReminderNotificationService notifications,
        ConfirmationReminderOptions options,
        ITenantContext tenant,
        TimeProvider clock,
        ILogger<ConfirmationReminderProcessor> logger)
    {
        _db = db;
        _resolver = resolver;
        _notifications = notifications;
        _options = options;
        _tenant = tenant;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ConfirmationReminderProcessingResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
            return new(0, 0, 0, 0);

        var now = _clock.GetUtcNow().UtcDateTime;
        var setting = await _db.ConfirmationSchedulerSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        // Per-tenant master switch and frequency gate. A null setting means "enabled with global defaults".
        if (setting is { Enabled: false })
            return new(0, 0, 0, 0);
        var frequencyMinutes = setting?.FrequencyMinutes ?? _options.DefaultFrequencyMinutes;
        if (setting?.LastRunAtUtc is DateTime last && last.AddMinutes(frequencyMinutes) > now)
            return new(0, 0, 0, 0);

        var maxAttempts = Math.Max(1, setting?.MaxAttempts ?? _options.MaxAttempts);
        var leaseMinutes = Math.Max(1, setting?.ClaimLeaseMinutes ?? _options.ClaimLeaseMinutes);
        var resendProcessed = setting?.ResendProcessed ?? false;
        var toggles = new ConfirmationRecipientToggles(
            setting?.NotifyEmployee ?? true, setting?.NotifyManager ?? true, setting?.NotifyHrbp ?? true);
        var fallbackTriggerDays = setting?.DefaultTriggerDaysBeforeEnd > 0 ? setting.DefaultTriggerDaysBeforeEnd : _options.DefaultTriggerDaysBeforeEnd;

        var today = DateOnly.FromDateTime(now);

        // Candidates: active employees not yet confirmed. Confirmed employees (ConfirmationDate set) and
        // inactive/terminated employees are excluded here so no reminder is raised for them.
        var candidates = await _db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active && (e.Employment == null || e.Employment.ConfirmationDate == null))
            .OrderBy(e => e.DateOfJoining).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.FirstName, e.MiddleName, e.LastName })
            .ToListAsync(cancellationToken);

        var cases = 0;
        var sent = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = await _resolver.ResolveAsync(tenantId, candidate.Id, today, cancellationToken);
            if (resolution.Status != ConfirmationPolicyResolutionStatus.Resolved
                || resolution.ProbationEndDate is not DateOnly probationEnd
                || resolution.DateOfJoining is not DateOnly doj
                || resolution.ConfirmationPolicyId is not Guid policyId
                || resolution.ConfirmationPolicyVersionId is not Guid versionId)
                continue; // no applicable policy (manual fallback) or ambiguous configuration

            var triggerDays = resolution.TriggerDaysBeforeEnd is > 0 ? resolution.TriggerDaysBeforeEnd!.Value : fallbackTriggerDays;
            var triggerDate = ConfirmationDateCalculator.ReviewTriggerDate(doj, probationEnd, triggerDays);

            // Only within the review window: on/after the trigger date and on/before probation end.
            if (today < triggerDate || today > probationEnd) continue;
            cases++;

            var policyName = await _db.ConfirmationPolicies.AsNoTracking()
                .Where(x => x.Id == policyId).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken) ?? "Confirmation policy";
            var reviewCase = new ConfirmationReminderCase(tenantId, candidate.Id,
                FullName(candidate.FirstName, candidate.MiddleName, candidate.LastName),
                policyId, versionId, policyName, probationEnd, triggerDate);

            var recipients = await _notifications.ResolveRecipientsAsync(reviewCase, toggles, cancellationToken);
            if (recipients.Count == 0)
            {
                skipped++;
                _logger.LogInformation("Confirmation reminder: no resolvable recipient for employee {EmployeeId}; nothing sent.", candidate.Id);
                continue;
            }

            foreach (var recipient in recipients)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var occurrenceKey = OccurrenceKey(candidate.Id, probationEnd, recipient);
                var claimToken = Guid.NewGuid();
                var claimed = await TryClaimAsync(reviewCase, recipient, occurrenceKey, claimToken, now, maxAttempts, leaseMinutes, resendProcessed, cancellationToken);
                if (!claimed) { skipped++; continue; }

                try
                {
                    var delivery = await _notifications.SendAsync(reviewCase, recipient, cancellationToken);
                    if (delivery == ConfirmationNotificationDeliveryResult.Failed)
                    {
                        failed++;
                        await FailAsync(tenantId, occurrenceKey, claimToken, new InvalidOperationException("Notification provider failure."), now, cancellationToken);
                    }
                    else
                    {
                        await CompleteAsync(tenantId, occurrenceKey, claimToken, delivery == ConfirmationNotificationDeliveryResult.Sent, now, cancellationToken);
                        if (delivery == ConfirmationNotificationDeliveryResult.Sent) sent++; else skipped++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;
                    await FailAsync(tenantId, occurrenceKey, claimToken, exception, now, cancellationToken);
                    _logger.LogWarning(exception, "Confirmation reminder failed for employee {EmployeeId} recipient {RecipientType}.", candidate.Id, recipient.Type);
                }
            }
        }

        await MarkTenantRunAsync(tenantId, setting, now, cancellationToken);
        return new(cases, sent, skipped, failed);
    }

    private async Task<bool> TryClaimAsync(
        ConfirmationReminderCase reviewCase, ConfirmationReminderRecipient recipient, string occurrenceKey, Guid claimToken,
        DateTime now, int maxAttempts, int leaseMinutes, bool resendProcessed, CancellationToken ct)
    {
        var tenantId = reviewCase.TenantId;
        var existing = await _db.ConfirmationReminderDeliveries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OccurrenceKey == occurrenceKey, ct);
        if (existing is not null)
        {
            // A successfully processed occurrence is never resent unless the tenant explicitly opts in.
            if (existing.Status == Sent && !resendProcessed) return false;
            if (existing.Status == Skipped) return false;
            if (existing.AttemptCount >= maxAttempts && existing.Status != Sent) return false;
            if (existing.Status == Claimed && existing.LeaseExpiresAtUtc > now) return false;
            if (existing.Status == Failed && existing.NextAttemptAtUtc > now) return false;

            // The predicate is the claim: two workers that both saw an expired lease cannot both acquire it.
            var updated = await _db.ConfirmationReminderDeliveries
                .Where(x => x.TenantId == tenantId && x.Id == existing.Id
                    && x.Status != Skipped
                    && (resendProcessed || x.Status != Sent)
                    && (x.Status == Sent || x.AttemptCount < maxAttempts)
                    && (x.Status != Claimed || x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now)
                    && (x.Status != Failed || x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, Claimed)
                    .SetProperty(x => x.RecipientEmployeeId, recipient.RecipientEmployeeId)
                    .SetProperty(x => x.RecipientEmail, recipient.Email)
                    .SetProperty(x => x.TriggerDate, reviewCase.TriggerDate)
                    .SetProperty(x => x.ClaimedAtUtc, now)
                    .SetProperty(x => x.LeaseExpiresAtUtc, now.AddMinutes(leaseMinutes))
                    .SetProperty(x => x.ClaimToken, claimToken)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.SentAtUtc, (DateTime?)null)
                    .SetProperty(x => x.ProcessedAtUtc, (DateTime?)null)
                    .SetProperty(x => x.LastError, (string?)null), ct);
            return updated == 1;
        }

        await using var transaction = await _db.BeginTransactionAsync(ct);
        try
        {
            _db.ConfirmationReminderDeliveries.Add(new ConfirmationReminderDelivery
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = reviewCase.EmployeeId,
                ConfirmationPolicyId = reviewCase.ConfirmationPolicyId, ConfirmationPolicyVersionId = reviewCase.ConfirmationPolicyVersionId,
                ProbationEndDate = reviewCase.ProbationEndDate, TriggerDate = reviewCase.TriggerDate,
                RecipientType = recipient.Type, RecipientEmployeeId = recipient.RecipientEmployeeId, RecipientEmail = recipient.Email,
                OccurrenceKey = occurrenceKey, NotificationType = NotificationType, Status = Claimed,
                DueAtUtc = now, ClaimedAtUtc = now, LeaseExpiresAtUtc = now.AddMinutes(leaseMinutes),
                ClaimToken = claimToken, AttemptCount = 1
            });
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the insert race on the unique (TenantId, OccurrenceKey) index: another worker owns it.
            return false;
        }
    }

    private async Task CompleteAsync(Guid tenantId, string occurrenceKey, Guid claimToken, bool delivered, DateTime now, CancellationToken ct)
    {
        await _db.ConfirmationReminderDeliveries
            .Where(x => x.TenantId == tenantId && x.OccurrenceKey == occurrenceKey && x.ClaimToken == claimToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, delivered ? Sent : Skipped)
                .SetProperty(x => x.SentAtUtc, delivered ? now : (DateTime?)null)
                .SetProperty(x => x.ProcessedAtUtc, now)
                .SetProperty(x => x.ClaimedAtUtc, (DateTime?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.ClaimToken, (Guid?)null)
                .SetProperty(x => x.NextAttemptAtUtc, (DateTime?)null), ct);
    }

    private async Task FailAsync(Guid tenantId, string occurrenceKey, Guid claimToken, Exception exception, DateTime now, CancellationToken ct)
    {
        await _db.ConfirmationReminderDeliveries
            .Where(x => x.TenantId == tenantId && x.OccurrenceKey == occurrenceKey && x.ClaimToken == claimToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, Failed)
                .SetProperty(x => x.LastError, exception.GetType().Name)
                .SetProperty(x => x.ProcessedAtUtc, now)
                .SetProperty(x => x.ClaimedAtUtc, (DateTime?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.ClaimToken, (Guid?)null)
                .SetProperty(x => x.NextAttemptAtUtc, now.AddMinutes(_options.RetryBackoffMinutes)), ct);
    }

    private async Task MarkTenantRunAsync(Guid tenantId, ConfirmationSchedulerSetting? setting, DateTime now, CancellationToken ct)
    {
        if (setting is not null)
        {
            await _db.ConfirmationSchedulerSettings
                .Where(x => x.TenantId == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastRunAtUtc, now), ct);
        }
        // With no settings row the tenant runs on every eligible tick (global default frequency is enforced by
        // the worker interval); a row is only created when an admin configures the scheduler.
    }

    private static string OccurrenceKey(Guid employeeId, DateOnly probationEnd, ConfirmationReminderRecipient recipient)
    {
        var discriminator = recipient.RecipientEmployeeId is Guid id ? id.ToString("N") : ShortHash(recipient.Email);
        return $"{employeeId:N}:{probationEnd:yyyyMMdd}:{(int)recipient.Type}:{discriminator}";
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string FullName(string first, string? middle, string last) =>
        string.Join(" ", new[] { first, middle, last }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
