using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed record NormalizedAttendancePunch(
    string ExternalEventId,
    string ExternalEmployeeIdentifier,
    DateTimeOffset OccurredAt,
    AttendanceDevicePunchDirection Direction,
    string? RawMetadata = null);

public sealed record AttendanceDeviceBatchRequest(
    Guid DeviceId,
    string Source,
    IReadOnlyList<NormalizedAttendancePunch> Punches,
    string? CheckpointAfter = null,
    Guid? LeaseToken = null);

public sealed record AttendanceDeviceItemResult(string ExternalEventId, AttendanceDeviceIngestionStatus Status, string? Message = null);

public sealed record AttendanceDeviceBatchResult(
    Guid SyncRunId,
    int Received,
    int Accepted,
    int Duplicate,
    int Rejected,
    int Unmapped,
    IReadOnlyList<AttendanceDeviceItemResult> Items,
    string? Checkpoint);

public interface IAttendancePunchSource
{
    string ProviderKey { get; }
    Task<AttendanceDevicePunchPage> FetchAsync(string? checkpoint, int maximumItems, CancellationToken cancellationToken = default);
}

public sealed record AttendanceDevicePunchPage(IReadOnlyList<NormalizedAttendancePunch> Punches, string? NextCheckpoint);

public interface IAttendanceDeviceIntegrationService
{
    Task<Result<AttendanceDeviceBatchResult>> IngestAsync(AttendanceDeviceBatchRequest request, CancellationToken cancellationToken = default);
}

public sealed record AttendanceDeviceDto(Guid Id, string Code, string Name, string DeviceType, string? Vendor,
    string? SerialNumber, Guid? WorkLocationId, string TimeZoneId, AttendanceDeviceConnectionMode ConnectionMode,
    AttendanceDeviceStatus Status, DateTime? LastSuccessfulSyncAtUtc, DateTime? LastAttemptedSyncAtUtc,
    DateTime? LastFailureAtUtc = null, string? LastFailureCode = null, int ConsecutiveFailureCount = 0, DateTime? NextRetryAtUtc = null);
public sealed record AttendanceDeviceRequest(string Code, string Name, string DeviceType, string? Vendor,
    string? SerialNumber, Guid? WorkLocationId, string TimeZoneId, AttendanceDeviceConnectionMode ConnectionMode,
    string? CredentialReference);
public sealed record AttendanceDeviceQuery(int Page = 1, int PageSize = 50, string? Search = null, AttendanceDeviceStatus? Status = null, AttendanceDeviceConnectionMode? ConnectionMode = null, DateTime? EligibleAtUtc = null);
public sealed record AttendanceDeviceMappingRequest(Guid DeviceId, string ExternalEmployeeIdentifier,
    Guid EmployeeId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, AttendanceDeviceMappingStatus Status);
public sealed record AttendanceDeviceMappingDto(Guid Id, Guid DeviceId, string ExternalEmployeeIdentifier,
    Guid EmployeeId, string EmployeeCode, DateOnly EffectiveFrom, DateOnly? EffectiveTo, AttendanceDeviceMappingStatus Status);
public sealed record AttendanceDeviceMappingQuery(Guid? DeviceId = null, string? ExternalEmployeeIdentifier = null,
    Guid? EmployeeId = null, int Page = 1, int PageSize = 50);
public sealed record AttendanceDeviceIssueQuery(AttendanceDeviceIngestionStatus? Status = null, Guid? DeviceId = null,
    Guid? EmployeeId = null, string? ExternalEmployeeIdentifier = null, Guid? SyncRunId = null,
    DateTime? FromUtc = null, DateTime? ToUtc = null, int Page = 1, int PageSize = 50);
public sealed record AttendanceDeviceIssueDto(Guid Id, Guid? DeviceId, Guid? SyncRunId, Guid? EmployeeId,
    Guid? AttendancePunchId, string ExternalEventId, string ExternalEmployeeIdentifier, DateTime OccurredAtUtc,
    DateTime ReceivedAtUtc, AttendanceDevicePunchDirection Direction, AttendanceDeviceIngestionStatus Status,
    string? SanitizedError);
public sealed record AttendanceDeviceSyncRunQuery(Guid? DeviceId = null, AttendanceDeviceSyncStatus? Status = null,
    DateTime? FromUtc = null, DateTime? ToUtc = null, int Page = 1, int PageSize = 50);
public sealed record AttendanceDeviceSyncRunDto(Guid Id, Guid? DeviceId, string Source, AttendanceDeviceSyncStatus Status,
    DateTime StartedAtUtc, DateTime? CompletedAtUtc, int Received, int Accepted, int Duplicate, int Rejected,
    int Unmapped, int ErrorCount, string? CheckpointBefore, string? CheckpointAfter,
    int AttemptNumber = 1, string? FailureCode = null, string? FailureMessage = null);
public sealed record AttendanceDeviceAuditQuery(Guid? DeviceId = null, Guid? MappingId = null, int Page = 1, int PageSize = 50);
public sealed record AttendanceDeviceAuditDto(Guid Id, Guid? ActorUserId, Guid? DeviceId, Guid? MappingId,
    Guid? EmployeeId, string Action, DateTime OccurredAtUtc, string ContextJson);

public interface IAttendanceDeviceOperationsService
{
    Task<Result<PagedResult<AttendanceDeviceDto>>> GetDevicesAsync(AttendanceDeviceQuery query, CancellationToken ct = default);
    Task<Result<AttendanceDeviceDto>> GetDeviceAsync(Guid id, CancellationToken ct = default);
    Task<Result<AttendanceDeviceDto>> CreateDeviceAsync(AttendanceDeviceRequest request, CancellationToken ct = default);
    Task<Result<AttendanceDeviceDto>> UpdateDeviceAsync(Guid id, AttendanceDeviceRequest request, CancellationToken ct = default);
    Task<Result<AttendanceDeviceDto>> SetDeviceStatusAsync(Guid id, AttendanceDeviceStatus status, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceDeviceMappingDto>>> GetMappingsAsync(AttendanceDeviceMappingQuery query, CancellationToken ct = default);
    Task<Result<AttendanceDeviceMappingDto>> CreateMappingAsync(AttendanceDeviceMappingRequest request, CancellationToken ct = default);
    Task<Result<AttendanceDeviceMappingDto>> UpdateMappingAsync(Guid id, AttendanceDeviceMappingRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeactivateMappingAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceDeviceIssueDto>>> GetIssuesAsync(AttendanceDeviceIssueQuery query, CancellationToken ct = default);
    Task<Result<AttendanceDeviceBatchResult>> ReprocessIssueAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceDeviceSyncRunDto>>> GetSyncRunsAsync(AttendanceDeviceSyncRunQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceDeviceAuditDto>>> GetAuditHistoryAsync(AttendanceDeviceAuditQuery query, CancellationToken ct = default);
    Task<Result<AttendanceDeviceBatchResult>> SyncNowAsync(Guid deviceId, CancellationToken ct = default);
}

public sealed class AttendanceDeviceWorkerOptions
{
    public const string SectionName = "AttendanceDeviceWorker";
    public bool Enabled { get; init; }
    public int PollingIntervalSeconds { get; init; } = 60;
    public int DevicePageSize { get; init; } = 50;
    public int MaxDevicesPerCycle { get; init; } = 100;
    public int BatchSize { get; init; } = 1000;
    public int LeaseDurationSeconds { get; init; } = 120;
    public int HeartbeatIntervalSeconds { get; init; } = 30;
    public int MaxRetries { get; init; } = 2;
    public int RetryBaseDelaySeconds { get; init; } = 5;
    public int RetryMaxDelaySeconds { get; init; } = 60;
    public int StaleRunThresholdSeconds { get; init; } = 300;

    public string? Validate()
    {
        if (PollingIntervalSeconds <= 0) return "Attendance device polling interval must be positive.";
        if (DevicePageSize is <= 0 or > 200) return "Attendance device page size must be between 1 and 200.";
        if (MaxDevicesPerCycle <= 0) return "Attendance device cycle limit must be positive.";
        if (BatchSize <= 0) return "Attendance device batch size must be positive.";
        if (LeaseDurationSeconds <= 0) return "Attendance device lease duration must be positive.";
        if (HeartbeatIntervalSeconds <= 0 || HeartbeatIntervalSeconds >= LeaseDurationSeconds)
            return "Attendance device heartbeat interval must be positive and shorter than the lease duration.";
        if (MaxRetries < 0) return "Attendance device retry count cannot be negative.";
        if (MaxRetries > 0 && RetryBaseDelaySeconds <= 0) return "Attendance device retry base delay must be positive when retries are enabled.";
        if (RetryMaxDelaySeconds < RetryBaseDelaySeconds) return "Attendance device retry maximum delay cannot be less than its base delay.";
        if (StaleRunThresholdSeconds <= 0) return "Attendance device stale-run threshold must be positive.";
        return null;
    }
}

public enum AttendanceDeviceFailureKind { Transient, Permanent }
public sealed record AttendanceDeviceFailure(AttendanceDeviceFailureKind Kind, string Code, string Message);

public interface IAttendanceDeviceSyncExecutionService
{
    Task<Result<AttendanceDeviceBatchResult>> ExecuteAsync(Guid deviceId, CancellationToken ct = default);
}

public interface IAttendanceDeviceRecoveryService
{
    Task<int> ReconcileStaleRunsAsync(CancellationToken ct = default);
}

public sealed record AttendanceDeviceHealthDto(bool WorkerEnabled, int ActiveDevices, int ActiveLeases,
    int StaleLeases, int StaleRunningSyncRuns, int RecentFailures);

public interface IAttendanceDeviceHealthService
{
    Task<Result<AttendanceDeviceHealthDto>> GetAsync(CancellationToken ct = default);
}

public sealed record AttendanceDeviceLeaseHandle(Guid DeviceId, Guid LeaseToken, string Owner, DateTime ExpiresAtUtc);
public sealed record AttendanceDeviceLeaseResult(bool Acquired, AttendanceDeviceLeaseHandle? Lease, string? Message);

public interface IAttendanceDeviceLeaseService
{
    Task<AttendanceDeviceLeaseResult> TryAcquireAsync(Guid deviceId, string owner, CancellationToken ct = default);
    Task<bool> HeartbeatAsync(Guid deviceId, Guid leaseToken, CancellationToken ct = default);
    Task<bool> ReleaseAsync(Guid deviceId, Guid leaseToken, CancellationToken ct = default);
}
