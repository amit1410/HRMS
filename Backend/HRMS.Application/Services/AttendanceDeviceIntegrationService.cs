using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceDeviceIntegrationService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IAttendancePunchIngestionService punchIngestion,
    IAttendanceDayProcessor dayProcessor,
    TimeProvider? timeProvider = null,
    IAttendanceBusinessTimeZoneProvider? businessTimeZoneProvider = null) : IAttendanceDeviceIntegrationService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly IAttendanceBusinessTimeZoneProvider _businessTimeZoneProvider = businessTimeZoneProvider ?? new AttendanceBusinessTimeZoneProvider();

    public async Task<Result<AttendanceDeviceBatchResult>> IngestAsync(AttendanceDeviceBatchRequest request, CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            return Result<AttendanceDeviceBatchResult>.Unauthorized("No authenticated tenant.");
        if (request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Source) || request.Source.Trim().Length > 60 || request.Punches is null || request.Punches.Count > 1000)
            return Result<AttendanceDeviceBatchResult>.Invalid("Device and a batch of at most 1000 events are required.");

        var device = await db.AttendanceDevices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.DeviceId, cancellationToken);
        if (device is null) return Result<AttendanceDeviceBatchResult>.NotFound("Attendance device was not found in this tenant.");
        if (device.Status != AttendanceDeviceStatus.Active) return Result<AttendanceDeviceBatchResult>.Conflict("Inactive or disabled devices cannot ingest punches.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var run = new AttendanceDeviceSyncRun
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = device.Id,
            Source = request.Source.Trim(), StartedAtUtc = now,
            CheckpointBefore = device.LastSuccessfulCheckpoint
        };
        db.AttendanceDeviceSyncRuns.Add(run);
        device.LastAttemptedSyncAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var results = new List<AttendanceDeviceItemResult>(request.Punches.Count);
        foreach (var input in request.Punches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceIdentity = $"device:{device.Id:N}:{request.Source.Trim()}";
            if (string.IsNullOrWhiteSpace(input.ExternalEventId) || input.ExternalEventId.Length > 200 ||
                string.IsNullOrWhiteSpace(input.ExternalEmployeeIdentifier) || input.ExternalEmployeeIdentifier.Length > 200 ||
                !TimeZoneInfo.TryFindSystemTimeZoneById(device.TimeZoneId, out _) || input.OccurredAt == default)
            {
                await SaveIssueAsync(new AttendanceDeviceIngestionEvent
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = device.Id, AttendanceDeviceSyncRunId = run.Id,
                    Source = sourceIdentity, ExternalEventId = string.IsNullOrWhiteSpace(input.ExternalEventId) ? Guid.NewGuid().ToString("N") : input.ExternalEventId,
                    ExternalEmployeeIdentifier = input.ExternalEmployeeIdentifier ?? string.Empty, OccurredAtUtc = input.OccurredAt.UtcDateTime,
                    ReceivedAtUtc = now, Direction = input.Direction, Status = AttendanceDeviceIngestionStatus.Rejected,
                    SanitizedError = "Malformed identity, timestamp, or device time-zone configuration."
                }, cancellationToken);
                results.Add(new(input.ExternalEventId ?? string.Empty, AttendanceDeviceIngestionStatus.Rejected, "Malformed event."));
                continue;
            }

            var prior = await db.AttendanceDeviceIngestionEvents.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Source == sourceIdentity && x.ExternalEventId == input.ExternalEventId, cancellationToken);
            if (prior is not null)
            {
                results.Add(new(input.ExternalEventId, AttendanceDeviceIngestionStatus.Duplicate, "Event was already durably received."));
                continue;
            }

            var businessCandidate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(input.OccurredAt, _businessTimeZoneProvider.GetTimeZone(tenantId)).DateTime);
            var mappings = await db.AttendanceDeviceEmployeeMappings.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.AttendanceDeviceId == device.Id &&
                    x.ExternalEmployeeIdentifier == input.ExternalEmployeeIdentifier && x.Status == AttendanceDeviceMappingStatus.Active &&
                    x.EffectiveFrom <= businessCandidate && (x.EffectiveTo == null || x.EffectiveTo >= businessCandidate))
                .OrderByDescending(x => x.EffectiveFrom).Take(2).ToListAsync(cancellationToken);
            var ambiguousMapping = mappings.Count > 1;
            var mapping = mappings.Count == 1 ? mappings[0] : null;

            var state = ambiguousMapping ? AttendanceDeviceIngestionStatus.Rejected : mapping is null ? AttendanceDeviceIngestionStatus.Unmapped : AttendanceDeviceIngestionStatus.Accepted;
            string? error = ambiguousMapping ? "Multiple effective mappings exist; no employee was selected." : mapping is null ? "No effective active employee mapping exists." : null;
            Guid? punchId = null;
            Guid? employeeId = mapping?.EmployeeId;
            if (ambiguousMapping)
            {
                // Retain the receipt without choosing an employee when the mapping data is ambiguous.
            }
            else if (mapping is not null && input.Direction is not (AttendanceDevicePunchDirection.In or AttendanceDevicePunchDirection.Out))
            {
                state = AttendanceDeviceIngestionStatus.Rejected;
                error = "Punch direction is unknown; it was retained without creating an authoritative raw punch.";
            }
            else if (mapping is not null && await IsFinalizedAsync(tenantId, businessCandidate, cancellationToken))
            {
                state = AttendanceDeviceIngestionStatus.RequiresPeriodReopen;
                error = "The Attendance period is finalized; controlled reopen is required before processing this late punch.";
            }
            else if (mapping is not null)
            {
                var externalPunchId = $"{device.Id:N}:{input.ExternalEventId}";
                var ingested = await punchIngestion.IngestAsync(new(
                    mapping.EmployeeId, input.OccurredAt.UtcDateTime,
                    input.Direction == AttendanceDevicePunchDirection.In ? PunchDirection.In : PunchDirection.Out,
                    PunchSource.Biometric, externalPunchId, device.Id.ToString("N"),
                    RawReference: "AttendanceDeviceIngestion"), cancellationToken);
                if (!ingested.Succeeded)
                {
                    state = AttendanceDeviceIngestionStatus.Rejected;
                    error = "Authoritative Attendance punch ingestion was rejected.";
                }
                else
                {
                    punchId = ingested.Value!.Id;
                    if (ingested.Message.StartsWith("Duplicate punch", StringComparison.OrdinalIgnoreCase)) state = AttendanceDeviceIngestionStatus.Duplicate;
                    else
                    {
                        var processed = await dayProcessor.ProcessAsync(mapping.EmployeeId, ingested.Value.BusinessDate, cancellationToken);
                        if (!processed.Succeeded)
                        {
                            state = AttendanceDeviceIngestionStatus.RequiresPeriodReopen;
                            error = "Raw punch is retained; daily Attendance processing requires an operational follow-up.";
                        }
                    }
                }
            }

            await SaveIssueAsync(new AttendanceDeviceIngestionEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = device.Id, AttendanceDeviceSyncRunId = run.Id,
                EmployeeId = employeeId, AttendancePunchId = punchId, Source = sourceIdentity, ExternalEventId = input.ExternalEventId,
                ExternalEmployeeIdentifier = input.ExternalEmployeeIdentifier, OccurredAtUtc = input.OccurredAt.UtcDateTime,
                ReceivedAtUtc = now, Direction = input.Direction, Status = state, SanitizedError = error
            }, cancellationToken);
            results.Add(new(input.ExternalEventId, state, error));
        }

        var accepted = results.Count(x => x.Status == AttendanceDeviceIngestionStatus.Accepted);
        var duplicate = results.Count(x => x.Status == AttendanceDeviceIngestionStatus.Duplicate);
        var unmapped = results.Count(x => x.Status == AttendanceDeviceIngestionStatus.Unmapped);
        var rejected = results.Count - accepted - duplicate - unmapped;
        run.ReceivedCount = results.Count; run.AcceptedCount = accepted; run.DuplicateCount = duplicate;
        run.UnmappedCount = unmapped; run.RejectedCount = rejected; run.ErrorCount = rejected;
        run.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
        run.Status = rejected == 0 && unmapped == 0 ? AttendanceDeviceSyncStatus.Succeeded :
            accepted + duplicate + unmapped > 0 ? AttendanceDeviceSyncStatus.PartiallySucceeded : AttendanceDeviceSyncStatus.Failed;
        // The checkpoint is persisted only after every receipt is durable. Rejected/unmapped receipts are
        // retained for operator remediation, so they do not cause silent event loss on the next poll.
        if (request.CheckpointAfter is { Length: <= 1000 } checkpoint && rejected == 0)
        {
            device.LastSuccessfulCheckpoint = checkpoint;
            device.LastSuccessfulSyncAtUtc = run.CompletedAtUtc;
            run.CheckpointAfter = checkpoint;
        }
        await db.SaveChangesAsync(cancellationToken);
        return Result<AttendanceDeviceBatchResult>.Success(new(run.Id, results.Count, accepted, duplicate, rejected, unmapped, results, run.CheckpointAfter));
    }

    private async Task<bool> IsFinalizedAsync(Guid tenantId, DateOnly date, CancellationToken ct) =>
        await db.AttendancePeriods.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.StartDate <= date && x.EndDate >= date && x.Status == AttendancePeriodStatus.Closed, ct);

    private async Task SaveIssueAsync(AttendanceDeviceIngestionEvent item, CancellationToken ct)
    {
        db.AttendanceDeviceIngestionEvents.Add(item);
        await db.SaveChangesAsync(ct);
    }
}
