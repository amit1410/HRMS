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
    string? CheckpointAfter = null);

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
    AttendanceDeviceStatus Status, DateTime? LastSuccessfulSyncAtUtc, DateTime? LastAttemptedSyncAtUtc);
public sealed record AttendanceDeviceRequest(string Code, string Name, string DeviceType, string? Vendor,
    string? SerialNumber, Guid? WorkLocationId, string TimeZoneId, AttendanceDeviceConnectionMode ConnectionMode,
    string? CredentialReference);
public sealed record AttendanceDeviceQuery(int Page = 1, int PageSize = 50, string? Search = null, AttendanceDeviceStatus? Status = null);
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
    int Unmapped, int ErrorCount, string? CheckpointBefore, string? CheckpointAfter);
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
