using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class AttendanceDevice : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? SerialNumber { get; set; }
    public Guid? WorkLocationId { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public AttendanceDeviceConnectionMode ConnectionMode { get; set; }
    public AttendanceDeviceStatus Status { get; set; } = AttendanceDeviceStatus.Active;
    public string? CredentialReference { get; set; }
    public string? LastSuccessfulCheckpoint { get; set; }
    public DateTime? LastSuccessfulSyncAtUtc { get; set; }
    public DateTime? LastAttemptedSyncAtUtc { get; set; }
}

public sealed class AttendanceDeviceEmployeeMapping : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendanceDeviceId { get; set; }
    public string ExternalEmployeeIdentifier { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public AttendanceDeviceMappingStatus Status { get; set; } = AttendanceDeviceMappingStatus.Active;
}

public sealed class AttendanceDeviceSyncRun : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? AttendanceDeviceId { get; set; }
    public string Source { get; set; } = string.Empty;
    public AttendanceDeviceSyncStatus Status { get; set; } = AttendanceDeviceSyncStatus.Running;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ReceivedCount { get; set; }
    public int AcceptedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int RejectedCount { get; set; }
    public int UnmappedCount { get; set; }
    public int ErrorCount { get; set; }
    public string? CheckpointBefore { get; set; }
    public string? CheckpointAfter { get; set; }
}

/// <summary>Integration receipt/idempotency record. AttendancePunch remains the authoritative source punch.</summary>
public sealed class AttendanceDeviceIngestionEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? AttendanceDeviceId { get; set; }
    public Guid? AttendanceDeviceSyncRunId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? AttendancePunchId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public string ExternalEmployeeIdentifier { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public AttendanceDevicePunchDirection Direction { get; set; }
    public AttendanceDeviceIngestionStatus Status { get; set; }
    public string? SanitizedError { get; set; }
}

/// <summary>Durable, tenant-scoped audit for device and employee-device administration.</summary>
public sealed class AttendanceDeviceAuditEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? AttendanceDeviceId { get; set; }
    public Guid? AttendanceDeviceEmployeeMappingId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>Sanitized structured context; credential references and secret values are never written.</summary>
    public string ContextJson { get; set; } = "{}";
}
