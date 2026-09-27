using System.Net;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class AttendanceDeviceSyncRecoveryService(
    IAttendanceDeviceOperationsService operations,
    IHrmsDbContext db,
    ITenantContext tenant,
    AttendanceDeviceWorkerOptions options,
    TimeProvider clock,
    ILogger<AttendanceDeviceSyncRecoveryService> logger) : IAttendanceDeviceSyncExecutionService, IAttendanceDeviceRecoveryService
{
    public async Task<Result<AttendanceDeviceBatchResult>> ExecuteAsync(Guid deviceId, CancellationToken ct = default)
    {
        Result<AttendanceDeviceBatchResult>? lastResult = null;
        for (var attempt = 1; attempt <= options.MaxRetries + 1; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                lastResult = await operations.SyncNowAsync(deviceId, ct);
                if (lastResult.Succeeded)
                {
                    await RecordSuccessAsync(deviceId, ct);
                    return lastResult;
                }

                var failure = Classify(lastResult);
                await RecordFailureAsync(deviceId, attempt, failure, ct);
                if (failure.Kind == AttendanceDeviceFailureKind.Permanent || attempt > options.MaxRetries)
                    return lastResult;
                await DelayAsync(attempt, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsTransient(exception))
            {
                var failure = Classify(exception);
                await RecordFailureAsync(deviceId, attempt, failure, ct);
                logger.LogWarning(exception, "Transient Attendance device sync failure for {DeviceId}; attempt {AttemptNumber}.", deviceId, attempt);
                if (attempt > options.MaxRetries)
                    return Result<AttendanceDeviceBatchResult>.Unavailable("DeviceSyncRetryExhausted");
                await DelayAsync(attempt, ct);
            }
            catch (Exception exception)
            {
                var failure = new AttendanceDeviceFailure(AttendanceDeviceFailureKind.Permanent, "DeviceSyncFailure", "Device synchronization failed.");
                await RecordFailureAsync(deviceId, attempt, failure, ct);
                logger.LogError(exception, "Permanent Attendance device sync failure for {DeviceId}.", deviceId);
                return Result<AttendanceDeviceBatchResult>.Failure(ResultStatus.Conflict, failure.Code);
            }
        }

        return lastResult ?? Result<AttendanceDeviceBatchResult>.Unavailable("DeviceSyncRetryExhausted");
    }

    public async Task<int> ReconcileStaleRunsAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now.AddSeconds(-options.StaleRunThresholdSeconds);
        var staleRuns = await db.AttendanceDeviceSyncRuns
            .Where(x => x.TenantId == tenantId && x.Status == AttendanceDeviceSyncStatus.Running && x.StartedAtUtc <= cutoff)
            .ToListAsync(ct);
        var staleLeases = await db.AttendanceDeviceSyncLeases
            .Where(x => x.TenantId == tenantId && x.LeaseToken != null && x.LeaseExpiresAtUtc <= now)
            .ToListAsync(ct);

        foreach (var run in staleRuns)
        {
            run.Status = AttendanceDeviceSyncStatus.Failed;
            run.CompletedAtUtc = now;
            run.ErrorCount++;
            run.FailureCode = "WorkerLeaseExpired";
            run.FailureMessage = "The worker lease expired before the synchronization completed.";
        }
        foreach (var lease in staleLeases)
        {
            lease.LeaseOwner = null; lease.LeaseToken = null; lease.ClaimedAtUtc = null; lease.LeaseExpiresAtUtc = null;
            lease.LastHeartbeatAtUtc = now; lease.Version++;
        }
        if (staleRuns.Count != 0 || staleLeases.Count != 0) await db.SaveChangesAsync(ct);
        return staleRuns.Count;
    }

    private async Task DelayAsync(int attempt, CancellationToken ct)
    {
        var multiplier = Math.Pow(2, attempt - 1);
        var seconds = Math.Min(options.RetryMaxDelaySeconds, options.RetryBaseDelaySeconds * multiplier);
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    private async Task RecordSuccessAsync(Guid deviceId, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return;
        var device = await db.AttendanceDevices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId, ct);
        if (device is null) return;
        device.ConsecutiveFailureCount = 0; device.LastFailureCode = null; device.NextRetryAtUtc = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task RecordFailureAsync(Guid deviceId, int attempt, AttendanceDeviceFailure failure, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var device = await db.AttendanceDevices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId, ct);
        if (device is null) return;
        device.LastAttemptedSyncAtUtc = now; device.LastFailureAtUtc = now; device.LastFailureCode = failure.Code;
        device.ConsecutiveFailureCount++;
        device.NextRetryAtUtc = failure.Kind == AttendanceDeviceFailureKind.Transient && attempt <= options.MaxRetries
            ? now.AddSeconds(Math.Min(options.RetryMaxDelaySeconds, options.RetryBaseDelaySeconds * Math.Pow(2, attempt - 1))) : null;
        db.AttendanceDeviceSyncRuns.Add(new AttendanceDeviceSyncRun
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = deviceId, Source = "worker-retry",
            Status = AttendanceDeviceSyncStatus.Failed, StartedAtUtc = now, CompletedAtUtc = now,
            AttemptNumber = attempt, ErrorCount = 1, FailureCode = failure.Code, FailureMessage = failure.Message
        });
        await db.SaveChangesAsync(ct);
    }

    private static AttendanceDeviceFailure Classify(Result<AttendanceDeviceBatchResult> result) =>
        result.Message.Contains("UnsupportedProvider", StringComparison.OrdinalIgnoreCase) ||
        result.Message.Contains("DeviceSyncAlreadyRunning", StringComparison.OrdinalIgnoreCase) ||
        result.Message.Contains("Inactive", StringComparison.OrdinalIgnoreCase) ||
        result.Message.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
        result.Message.Contains("DeviceNotFound", StringComparison.OrdinalIgnoreCase)
            ? new(AttendanceDeviceFailureKind.Permanent,
                result.Message.Contains("DeviceSyncAlreadyRunning", StringComparison.OrdinalIgnoreCase) ? "LeaseContended" : "DeviceConfiguration",
                result.Message.Contains("DeviceSyncAlreadyRunning", StringComparison.OrdinalIgnoreCase) ? "Another worker currently owns this device synchronization." : "The device configuration does not permit synchronization.")
            : new(AttendanceDeviceFailureKind.Transient, "DeviceSyncUnavailable", "The device synchronization attempt was unavailable.");

    private static AttendanceDeviceFailure Classify(Exception exception) => exception switch
    {
        TimeoutException => new(AttendanceDeviceFailureKind.Transient, "ProviderTimeout", "The provider did not respond before the timeout."),
        HttpRequestException => new(AttendanceDeviceFailureKind.Transient, "ProviderUnavailable", "The provider was temporarily unavailable."),
        DbUpdateException => new(AttendanceDeviceFailureKind.Transient, "PersistenceTransient", "Persistence was temporarily unavailable."),
        _ => new(AttendanceDeviceFailureKind.Permanent, "DeviceSyncFailure", "Device synchronization failed.")
    };

    private static bool IsTransient(Exception exception) => exception is TimeoutException or HttpRequestException or DbUpdateException;
}
