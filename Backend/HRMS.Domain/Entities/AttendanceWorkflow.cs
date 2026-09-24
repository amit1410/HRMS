using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class AttendanceRegularizationRequest : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public AttendanceRegularizationType RequestType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? ProposedInAtUtc { get; set; }
    public DateTime? ProposedOutAtUtc { get; set; }
    public AttendanceRequestStatus Status { get; set; } = AttendanceRequestStatus.Pending;
    public int ConcurrencyVersion { get; set; } = 1;
    public Guid SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewerComments { get; set; }
    public ICollection<AttendanceRegularizationEvent> Events { get; set; } = new List<AttendanceRegularizationEvent>();
}

public sealed class AttendanceRegularizationEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendanceRegularizationRequestId { get; set; }
    public AttendanceRequestEventType EventType { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Comments { get; set; }
    public AttendanceRegularizationRequest? Request { get; set; }
}

public sealed class AttendanceAdjustment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public Guid AttendanceRegularizationRequestId { get; set; }
    public DateTime? EffectiveInAtUtc { get; set; }
    public DateTime? EffectiveOutAtUtc { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public DateTime ApprovedAtUtc { get; set; }
}

public sealed class AttendanceOnDutyRequest : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string? Location { get; set; }
    public AttendanceRequestStatus Status { get; set; } = AttendanceRequestStatus.Pending;
    public int ConcurrencyVersion { get; set; } = 1;
    public Guid SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewerComments { get; set; }
    public ICollection<AttendanceOnDutyEvent> Events { get; set; } = new List<AttendanceOnDutyEvent>();
}

public sealed class AttendanceOnDutyEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendanceOnDutyRequestId { get; set; }
    public AttendanceRequestEventType EventType { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Comments { get; set; }
    public AttendanceOnDutyRequest? Request { get; set; }
}

public sealed class AttendancePeriod : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public AttendancePeriodStatus Status { get; set; } = AttendancePeriodStatus.Open;
    public int DataVersion { get; set; } = 1;
    public int ConcurrencyVersion { get; set; } = 1;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public Guid? LastProcessedByUserId { get; set; }
    public Guid? ProcessingRunId { get; set; }
    public ICollection<AttendancePeriodEvent> Events { get; set; } = new List<AttendancePeriodEvent>();
}

public sealed class AttendancePeriodEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendancePeriodId { get; set; }
    public AttendancePeriodEventType EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid? ProcessingRunId { get; set; }
    public int DataVersion { get; set; }
    public string? Details { get; set; }
    public AttendancePeriod? Period { get; set; }
}

public sealed class EmployeeAttendanceMonthlySummary : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendancePeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public string? EmployeeCode { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int CalendarDays { get; set; }
    public int EmploymentDays { get; set; }
    public int WorkingDays { get; set; }
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int OnLeaveDays { get; set; }
    public int OnDutyDays { get; set; }
    public int HolidayDays { get; set; }
    public int WeeklyOffDays { get; set; }
    public int IncompleteDays { get; set; }
    public int NotProcessedDays { get; set; }
    public int LateInCount { get; set; }
    public int EarlyOutCount { get; set; }
    public int GraceAppliedCount { get; set; }
    public int MissingInCount { get; set; }
    public int MissingOutCount { get; set; }
    public int RegularizedDays { get; set; }
    public int ApprovedOnDutyDays { get; set; }
    public int LeaveConflictCount { get; set; }
    public int ExceptionCount { get; set; }
    public int ExpectedWorkMinutes { get; set; }
    public int ActualWorkMinutes { get; set; }
    public int SourceDataVersion { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public decimal PresentDayQuantity { get; set; }
    public decimal PaidLeaveDays { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public decimal PayableDays { get; set; }
    public decimal LopDays { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>Immutable payroll-facing attendance contract. Refinalization creates a new version.</summary>
public sealed class PayrollAttendanceSnapshot : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendancePeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? EmploymentId { get; set; }
    public int Version { get; set; }
    public bool IsCurrent { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal EligibleDays { get; set; }
    public decimal PayableDays { get; set; }
    public decimal LopDays { get; set; }
    public decimal PresentDays { get; set; }
    public decimal AbsentDays { get; set; }
    public decimal PaidLeaveDays { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public decimal HolidayDays { get; set; }
    public decimal WeekOffDays { get; set; }
    public decimal OnDutyDays { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public DateTime FinalizedAtUtc { get; set; }
    public string? SourceHash { get; set; }
    public AttendancePeriod? AttendancePeriod { get; set; }
    public Employee? Employee { get; set; }
}
