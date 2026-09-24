using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities.Separation;

public sealed class SeparationClearanceTemplate : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public SeparationType? AppliesToSeparationType { get; set; }
    public Guid? AppliesToReasonId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? ModifiedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public ICollection<SeparationClearanceTemplateItem> Items { get; set; } = new List<SeparationClearanceTemplateItem>();
}

public sealed class SeparationClearanceTemplateItem : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SeparationClearanceTaskCategory Category { get; set; }
    public ClearanceOwnerType OwnerType { get; set; }
    public Guid? OwnerReferenceId { get; set; }
    public bool IsMandatory { get; set; }
    public bool RequiresAssetReturn { get; set; }
    public bool RequiresComment { get; set; }
    public bool RequiresEvidence { get; set; }
    public int Sequence { get; set; }
    public int? DueDaysBeforeLwd { get; set; }
    public bool IsActive { get; set; } = true;
    public SeparationClearanceTemplate? Template { get; set; }
}

public sealed class SeparationClearance : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSeparationId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TemplateId { get; set; }
    public SeparationClearanceStatus Status { get; set; } = SeparationClearanceStatus.NotStarted;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ReopenedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public ICollection<SeparationClearanceTask> Tasks { get; set; } = new List<SeparationClearanceTask>();
    public ICollection<SeparationClearanceEvent> Events { get; set; } = new List<SeparationClearanceEvent>();
}

public sealed class SeparationClearanceTask : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SeparationClearanceId { get; set; }
    public Guid TemplateItemId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SeparationClearanceTaskCategory Category { get; set; }
    public ClearanceOwnerType OwnerType { get; set; }
    public Guid? AssignedUserId { get; set; }
    public Guid? AssignedRoleId { get; set; }
    public Guid? AssignedDepartmentId { get; set; }
    public bool IsMandatory { get; set; }
    public bool RequiresAssetReturn { get; set; }
    public bool RequiresComment { get; set; }
    public bool RequiresEvidence { get; set; }
    public SeparationClearanceTaskStatus Status { get; set; } = SeparationClearanceTaskStatus.Pending;
    public DateOnly? DueDate { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string? Comment { get; set; }
    public string? BlockingReason { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public SeparationClearance? Clearance { get; set; }
    public ICollection<SeparationAssetReturn> Assets { get; set; } = new List<SeparationAssetReturn>();
}

public sealed class SeparationAssetReturn : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SeparationClearanceTaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public string AssetReference { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpectedReturnDate { get; set; }
    public DateOnly? ActualReturnDate { get; set; }
    public SeparationAssetReturnStatus ReturnStatus { get; set; } = SeparationAssetReturnStatus.PendingReturn;
    public SeparationAssetCondition Condition { get; set; } = SeparationAssetCondition.Unknown;
    public bool RecoveryRequired { get; set; }
    public string? RecoveryReference { get; set; }
    public string? Comment { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
}

public sealed class SeparationClearanceEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SeparationClearanceId { get; set; }
    public Guid? TaskId { get; set; }
    public SeparationClearanceEventType EventType { get; set; }
    public SeparationClearanceStatus? FromStatus { get; set; }
    public SeparationClearanceStatus? ToStatus { get; set; }
    public SeparationClearanceTaskStatus? FromTaskStatus { get; set; }
    public SeparationClearanceTaskStatus? ToTaskStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
    public string? MetadataJson { get; set; }
    public SeparationClearance? Clearance { get; set; }
}
