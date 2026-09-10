using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class LeaveApprovalReminderProcessor : ILeaveApprovalReminderProcessor
{
    private const string NotificationType = "LeaveApprovalReminder";
    private const string Sent = "Sent";
    private const string Claimed = "Claimed";
    private const string Failed = "Failed";
    private const string Skipped = "Skipped";
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeManagerResolver _managerResolver;
    private readonly ILeaveNotificationService _notifications;
    private readonly LeaveReminderOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<LeaveApprovalReminderProcessor> _logger;

    public LeaveApprovalReminderProcessor(
        IHrmsDbContext db,
        IEmployeeManagerResolver managerResolver,
        ILeaveNotificationService notifications,
        LeaveReminderOptions options,
        TimeProvider clock,
        ILogger<LeaveApprovalReminderProcessor> logger)
    {
        _db = db;
        _managerResolver = managerResolver;
        _notifications = notifications;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LeaveReminderProcessingResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var threshold = now.AddHours(-_options.InitialDelayHours);
        var requests = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.Status == LeaveRequestStatus.PendingApproval && x.SubmittedAtUtc != null && x.SubmittedAtUtc <= threshold)
            .OrderBy(x => x.SubmittedAtUtc)
            .Take(_options.BatchSize)
            .Select(x => new ReminderCandidate(x.Id, x.TenantId, x.EmployeeId, x.StartDate, x.SubmittedAtUtc!.Value))
            .ToListAsync(cancellationToken);

        var sent = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manager = await _managerResolver.ResolveAsync(request.EmployeeId, request.StartDate, cancellationToken);
            if (!manager.Succeeded || manager.Value?.ManagerId is not Guid managerId)
            {
                skipped++;
                _logger.LogWarning("Skipping Leave approval reminder for request {LeaveRequestId}: no effective manager.", request.Id);
                continue;
            }

            var elapsed = now - request.SubmittedAtUtc.AddHours(_options.InitialDelayHours);
            var occurrence = elapsed.Ticks / TimeSpan.FromHours(_options.RepeatIntervalHours).Ticks;
            var occurrenceKey = $"{request.Id:N}:{occurrence}";
            var claim = await TryClaimAsync(request, managerId, occurrenceKey, now, cancellationToken);
            if (!claim) { skipped++; continue; }

            try
            {
                var delivery = await _notifications.NotifyApprovalReminderAsync(request.Id, cancellationToken);
                if (delivery == LeaveNotificationDeliveryResult.Failed)
                {
                    failed++;
                    await FailAsync(request.TenantId, occurrenceKey, new InvalidOperationException("Notification provider failure."), now, cancellationToken);
                }
                else
                {
                    await CompleteAsync(request.TenantId, occurrenceKey, delivery == LeaveNotificationDeliveryResult.Sent, now, cancellationToken);
                    if (delivery == LeaveNotificationDeliveryResult.Sent) sent++; else skipped++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                await FailAsync(request.TenantId, occurrenceKey, exception, now, cancellationToken);
                _logger.LogWarning(exception, "Leave approval reminder failed for request {LeaveRequestId}.", request.Id);
            }
        }

        return new(requests.Count, sent, skipped, failed);
    }

    private async Task<bool> TryClaimAsync(ReminderCandidate request, Guid managerId, string occurrenceKey, DateTime now, CancellationToken ct)
    {
        await using var transaction = await _db.BeginTransactionAsync(ct);
        var existing = await _db.LeaveReminderDeliveries.SingleOrDefaultAsync(x => x.OccurrenceKey == occurrenceKey, ct);
        if (existing is not null)
        {
            if (existing.Status == Sent || existing.Status == Skipped || existing.AttemptCount >= _options.MaxAttempts)
                return false;
            if (existing.Status == Claimed && existing.LeaseExpiresAtUtc > now)
                return false;
            if (existing.Status == Failed && existing.NextAttemptAtUtc > now)
                return false;
            existing.Status = Claimed;
            existing.RecipientEmployeeId = managerId;
            existing.ClaimedAtUtc = now;
            existing.LeaseExpiresAtUtc = now.AddMinutes(_options.ClaimLeaseMinutes);
            existing.AttemptCount++;
            existing.LastError = null;
        }
        else
        {
            _db.LeaveReminderDeliveries.Add(new LeaveReminderDelivery
            {
                Id = Guid.NewGuid(), TenantId = request.TenantId, LeaveRequestId = request.Id,
                RecipientEmployeeId = managerId, OccurrenceKey = occurrenceKey, NotificationType = NotificationType,
                Status = Claimed, DueAtUtc = request.SubmittedAtUtc.AddHours(_options.InitialDelayHours),
                ClaimedAtUtc = now, LeaseExpiresAtUtc = now.AddMinutes(_options.ClaimLeaseMinutes), AttemptCount = 1
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task CompleteAsync(Guid tenantId, string occurrenceKey, bool delivered, DateTime now, CancellationToken ct)
    {
        var row = await _db.LeaveReminderDeliveries.SingleAsync(x => x.TenantId == tenantId && x.OccurrenceKey == occurrenceKey, ct);
        row.Status = delivered ? Sent : Skipped;
        row.SentAtUtc = delivered ? now : null;
        row.ClaimedAtUtc = null;
        row.LeaseExpiresAtUtc = null;
        row.NextAttemptAtUtc = null;
        await _db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(Guid tenantId, string occurrenceKey, Exception exception, DateTime now, CancellationToken ct)
    {
        var row = await _db.LeaveReminderDeliveries.SingleAsync(x => x.TenantId == tenantId && x.OccurrenceKey == occurrenceKey, ct);
        row.Status = Failed;
        row.LastError = exception.GetType().Name;
        row.ClaimedAtUtc = null;
        row.LeaseExpiresAtUtc = null;
        row.NextAttemptAtUtc = now.AddHours(_options.RepeatIntervalHours);
        await _db.SaveChangesAsync(ct);
    }

    private sealed record ReminderCandidate(Guid Id, Guid TenantId, Guid EmployeeId, DateOnly StartDate, DateTime SubmittedAtUtc);
}
