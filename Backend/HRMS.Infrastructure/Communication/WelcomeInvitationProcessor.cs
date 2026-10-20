using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Communication;

public sealed class WelcomeInvitationProcessor(
    IHrmsDbContext db,
    IServiceProvider serviceProvider,
    WelcomeInvitationOptions options,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<WelcomeInvitationProcessor> logger,
    ITenantContext? tenantContext = null) : IWelcomeInvitationProcessor
{
    public async Task<WelcomeInvitationBatchPreview> PreviewBatchAsync(Guid tenantId, Guid batchId, CancellationToken ct = default)
    {
        EnsureTenant(tenantId);
        var now = clock.GetUtcNow().UtcDateTime;
        var eligible = await EligibleBatchQuery(tenantId, batchId, now).OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id)
            .Select(x => new WelcomeInvitationDryRunRow(x.Id, Mask(x.RecipientEmailSnapshot), x.DeliveryStatus.ToString())).ToListAsync(ct);
        var excluded = await db.WelcomeInvitationAttempts.Where(x => x.TenantId == tenantId && x.BatchId == batchId)
            .Where(x => !EligibleBatchQuery(tenantId, batchId, now).Any(y => y.Id == x.Id))
            .OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.RecipientEmailSnapshot, x.DeliveryStatus, x.NextAttemptAtUtc, x.LeaseExpiresAtUtc, x.RetryCount, x.MaxAttempts })
            .ToListAsync(ct);
        return new WelcomeInvitationBatchPreview(eligible, excluded.Select(x => new WelcomeInvitationExcludedRow(x.Id, Mask(x.RecipientEmailSnapshot),
            x.RetryCount >= x.MaxAttempts ? "Retry limit reached" : x.LeaseExpiresAtUtc > now ? "Active processing lease" : x.NextAttemptAtUtc > now ? "Retry scheduled for later" : $"Status {x.DeliveryStatus} is not eligible")).ToArray());
    }

    public async Task<int> ProcessBatchAsync(Guid tenantId, Guid batchId, bool operatorConfirmed, CancellationToken ct = default)
    {
        EnsureTenant(tenantId);
        if (!operatorConfirmed) throw new InvalidOperationException("Explicit operator confirmation is required for delivery.");
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await EligibleBatchQuery(tenantId, batchId, now).OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id)
            .Take(Math.Clamp(options.BatchSize, 1, 500)).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids) await ProcessAttemptScopedAsync(id, tenantId, batchId, ct);
        logger.LogInformation("Operator batch invitation run completed. TenantId={TenantId} BatchId={BatchId} Eligible={EligibleCount} Claimed={ProcessedCount} Outcome={Outcome}.", tenantId, batchId, ids.Count, ids.Count, "Completed");
        return ids.Count;
    }

    public async Task ProcessSingleAttemptAsync(Guid tenantId, Guid batchId, Guid attemptId, bool operatorConfirmed, CancellationToken ct = default)
    {
        EnsureTenant(tenantId);
        if (!operatorConfirmed) throw new InvalidOperationException("Explicit operator confirmation is required for delivery.");
        if (batchId == Guid.Empty || attemptId == Guid.Empty) throw new ArgumentException("Batch and attempt identifiers must be non-empty GUIDs.");
        var now = clock.GetUtcNow().UtcDateTime;
        var lease = Guid.NewGuid().ToString("N");
        var claimed = await db.WelcomeInvitationAttempts.Where(x => x.TenantId == tenantId && x.BatchId == batchId && x.Id == attemptId
                && (x.DeliveryStatus == WelcomeInvitationStatus.Queued || x.DeliveryStatus == WelcomeInvitationStatus.Failed || x.DeliveryStatus == WelcomeInvitationStatus.Processing)
                && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)
                && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now)
                && x.RetryCount < x.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.DeliveryStatus, WelcomeInvitationStatus.Processing)
                .SetProperty(x => x.LeaseId, lease)
                .SetProperty(x => x.LeaseExpiresAtUtc, now.AddSeconds(options.LeaseSeconds))
                .SetProperty(x => x.ProcessingStartedAtUtc, now)
                .SetProperty(x => x.LastAttemptAtUtc, now)
                .SetProperty(x => x.RetryCount, x => x.RetryCount + 1), ct);
        if (claimed != 1) throw new InvalidOperationException("Specified invitation attempt was not eligible in the specified tenant and batch; no attempt was processed.");
        await DeliverClaimedAttemptAsync(tenantId, batchId, attemptId, lease, ct);
    }

    private IQueryable<HRMS.Domain.Entities.WelcomeInvitationAttempt> EligibleBatchQuery(Guid tenantId, Guid batchId, DateTime now) => db.WelcomeInvitationAttempts
        .Where(x => x.TenantId == tenantId && x.BatchId == batchId
            && (x.DeliveryStatus == WelcomeInvitationStatus.Queued || x.DeliveryStatus == WelcomeInvitationStatus.Failed || x.DeliveryStatus == WelcomeInvitationStatus.Processing)
            && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)
            && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now)
            && x.RetryCount < x.MaxAttempts);

    private void EnsureTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty || tenantContext?.TenantId != tenantId)
            throw new InvalidOperationException("The explicitly selected tenant does not match the resolved tenant database context.");
    }

    private static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(missing)";
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var local = email[..at];
        return (local.Length == 1 ? "*" : local[..1] + new string('*', Math.Min(6, local.Length - 1))) + email[at..];
    }

    public async Task ProcessAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await db.WelcomeInvitationAttempts
            .Where(x => (x.DeliveryStatus == WelcomeInvitationStatus.Queued || x.DeliveryStatus == WelcomeInvitationStatus.Failed || x.DeliveryStatus == WelcomeInvitationStatus.Processing)
                && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)
                && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now)
                && x.RetryCount < x.MaxAttempts)
            .OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id)
            .Take(Math.Clamp(options.BatchSize, 1, 500))
            .Select(x => new { x.Id, x.BatchId }).ToListAsync(ct);

        var tenantId = tenantContext?.TenantId ?? Guid.Empty;
        logger.LogInformation("Welcome invitation eligible attempts selected. TenantId={TenantId} EligibleAttemptCount={EligibleAttemptCount}.", tenantId, candidates.Count);
        if (candidates.Count == 0)
            logger.LogInformation("Welcome invitation processing skipped because no attempts are eligible. TenantId={TenantId}.", tenantId);
        foreach (var candidate in candidates)
            await ProcessAttemptScopedAsync(candidate.Id, tenantId, candidate.BatchId, ct);
    }

    public async Task ProcessAttemptAsync(Guid id, CancellationToken ct = default)
        => await ProcessAttemptScopedAsync(id, tenantContext?.TenantId ?? Guid.Empty, null, ct);

    private async Task ProcessAttemptScopedAsync(Guid id, Guid tenantId, Guid? batchId, CancellationToken ct)
    {
            var now = clock.GetUtcNow().UtcDateTime;
            var lease = Guid.NewGuid().ToString("N");
            var claimed = await db.WelcomeInvitationAttempts.Where(x => x.Id == id && x.TenantId == tenantId
                    && (batchId == null || x.BatchId == batchId)
                    && (x.DeliveryStatus == WelcomeInvitationStatus.Queued || x.DeliveryStatus == WelcomeInvitationStatus.Failed || x.DeliveryStatus == WelcomeInvitationStatus.Processing)
                    && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now)
                    && x.RetryCount < x.MaxAttempts)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.DeliveryStatus, WelcomeInvitationStatus.Processing)
                    .SetProperty(x => x.LeaseId, lease)
                    .SetProperty(x => x.LeaseExpiresAtUtc, now.AddSeconds(options.LeaseSeconds))
                    .SetProperty(x => x.ProcessingStartedAtUtc, now)
                    .SetProperty(x => x.LastAttemptAtUtc, now)
                    .SetProperty(x => x.RetryCount, x => x.RetryCount + 1), ct);
            if (claimed != 1)
            {
                logger.LogInformation("Welcome invitation attempt skipped because it could not be claimed. TenantId={TenantId} BatchId={BatchId} AttemptId={AttemptId}.", tenantId, batchId, id);
                return;
            }

            logger.LogInformation("Welcome invitation attempt claimed. TenantId={TenantId} BatchId={BatchId} AttemptId={AttemptId}.", tenantId, batchId, id);

            await DeliverClaimedAttemptAsync(tenantId, batchId, id, lease, ct);
    }

    private async Task DeliverClaimedAttemptAsync(Guid tenantId, Guid? batchId, Guid id, string lease, CancellationToken ct)
    {
            var attempt = await db.WelcomeInvitationAttempts.SingleAsync(x => x.Id == id && x.TenantId == tenantId && (batchId == null || x.BatchId == batchId) && x.LeaseId == lease, ct);
            try
            {
                var accounts = serviceProvider.GetRequiredService<IEmployeePortalAccountService>();
                var result = await accounts.ProcessWelcomeInvitationAsync(attempt.EmployeeId, attempt.RequestedByUserId, attempt.AttemptType == WelcomeInvitationAttemptType.Resend, ct);
                if (result.Succeeded)
                {
                    var sentAt = clock.GetUtcNow().UtcDateTime;
                    await db.WelcomeInvitationAttempts.Where(x => x.Id == id && x.LeaseId == lease).ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.DeliveryStatus, WelcomeInvitationStatus.Sent)
                        .SetProperty(x => x.SentAtUtc, sentAt)
                        .SetProperty(x => x.CompletedAtUtc, sentAt)
                        .SetProperty(x => x.LeaseId, (string?)null)
                        .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);
                    logger.LogInformation("Welcome invitation attempt processed successfully. TenantId={TenantId} BatchId={BatchId} AttemptId={AttemptId} Outcome={Outcome}.", tenantId, attempt.BatchId, id, "Sent");
                }
                else
                {
                    await FailAsync(id, lease, result.Message, result.Status == HRMS.Application.Common.ResultStatus.ServiceUnavailable, ct);
                    logger.LogWarning("Welcome invitation attempt failed during delivery. TenantId={TenantId} BatchId={BatchId} AttemptId={AttemptId} FailureType={FailureType}.", tenantId, attempt.BatchId, id, result.Status.ToString());
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning("Welcome invitation attempt failed during delivery. TenantId={TenantId} BatchId={BatchId} AttemptId={AttemptId} ExceptionType={ExceptionType}.", tenantId, attempt.BatchId, id, ex.GetType().Name);
                await FailAsync(id, lease, "Welcome invitation delivery failed.", true, ct);
            }
            await UpdateBatchAsync(attempt.BatchId, ct);
    }

    private async Task FailAsync(Guid id, string lease, string message, bool retryable, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var attempt = await db.WelcomeInvitationAttempts.AsNoTracking().SingleAsync(x => x.Id == id && x.LeaseId == lease, ct);
        var exhausted = !retryable || attempt.RetryCount >= Math.Min(attempt.MaxAttempts, options.MaxAttempts);
        await db.WelcomeInvitationAttempts.Where(x => x.Id == id && x.LeaseId == lease).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.DeliveryStatus, WelcomeInvitationStatus.Failed)
            .SetProperty(x => x.ErrorCategory, retryable ? "TransientDeliveryFailure" : "PermanentDeliveryFailure")
            .SetProperty(x => x.ErrorSummary, message.Length > 500 ? message[..500] : message)
            .SetProperty(x => x.CompletedAtUtc, exhausted ? now : (DateTime?)null)
            .SetProperty(x => x.NextAttemptAtUtc, exhausted ? (DateTime?)null : now.AddSeconds(Math.Min(3600, options.RetryBaseSeconds * Math.Pow(2, Math.Max(0, attempt.RetryCount - 1)))))
            .SetProperty(x => x.LeaseId, (string?)null)
            .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);
    }

    private async Task UpdateBatchAsync(Guid? batchId, CancellationToken ct)
    {
        if (batchId is not Guid batch) return;
        var states = await db.WelcomeInvitationAttempts.Where(x => x.BatchId == batch).GroupBy(x => x.DeliveryStatus).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct);
        var pending = states.Where(x => x.Key is WelcomeInvitationStatus.Queued or WelcomeInvitationStatus.Processing).Sum(x => x.Count);
        var failed = states.SingleOrDefault(x => x.Key == WelcomeInvitationStatus.Failed)?.Count ?? 0;
        var completed = pending == 0;
        var now = clock.GetUtcNow().UtcDateTime;
        await db.WelcomeInvitationBatches.Where(x => x.Id == batch).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, completed ? (failed == 0 ? WelcomeInvitationBatchStatus.Completed : WelcomeInvitationBatchStatus.CompletedWithFailures) : WelcomeInvitationBatchStatus.Processing)
            .SetProperty(x => x.CompletedAtUtc, completed ? now : (DateTime?)null), ct);
    }
}
