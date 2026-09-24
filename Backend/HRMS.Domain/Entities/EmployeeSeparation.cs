using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities.Separation;

public sealed class SeparationReason : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SeparationReasonCategory Category { get; set; }
    public bool EmployeeInitiatedAllowed { get; set; } = true;
    public bool EmployerInitiatedAllowed { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int DisplayOrder { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<EmployeeSeparation> Separations { get; set; } = new List<EmployeeSeparation>();
}

public sealed class EmployeeSeparation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ActiveEmployeeKey { get; set; }
    public string SeparationNumber { get; set; } = string.Empty;
    public SeparationType SeparationType { get; set; }
    public Guid ReasonId { get; set; }
    public EmployeeSeparationInitiator InitiatedBy { get; set; }
    public Guid? InitiatedByUserId { get; set; }
    public DateOnly RequestDate { get; set; }
    public DateOnly ProposedLastWorkingDate { get; set; }
    public DateOnly? ApprovedLastWorkingDate { get; set; }
    public DateOnly? NoticeStartDate { get; set; }
    public DateOnly? NoticeEndDate { get; set; }
    public DateOnly? ExpectedNoticeEndDate { get; set; }
    public int? NoticePeriodDays { get; set; }
    public int? NoticeServedDays { get; set; }
    public int? NoticeShortfallDays { get; set; }
    public int WaivedNoticeDays { get; set; }
    public int NoticeExtensionDays { get; set; }
    public NoticeDisposition NoticeDisposition { get; set; } = NoticeDisposition.None;
    public DateTime? LastNoticeRevisionAtUtc { get; set; }
    public string? EmployeeRemarks { get; set; }
    public string? ManagerRemarks { get; set; }
    public string? HrRemarks { get; set; }
    public EmployeeSeparationStatus Status { get; set; } = EmployeeSeparationStatus.Draft;
    public Guid? CreatedByUserId { get; set; }
    public Guid? ModifiedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public SeparationReason? Reason { get; set; }
    public ICollection<EmployeeSeparationEvent> Events { get; set; } = new List<EmployeeSeparationEvent>();
}

public sealed class EmployeeSeparationEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSeparationId { get; set; }
    public EmployeeSeparationEventType EventType { get; set; }
    public EmployeeSeparationStatus? FromStatus { get; set; }
    public EmployeeSeparationStatus? ToStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
    public string? MetadataJson { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeSeparation? Separation { get; set; }
}
