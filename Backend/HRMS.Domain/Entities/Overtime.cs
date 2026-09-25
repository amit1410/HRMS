using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class OvertimePolicy : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int MinimumExtraMinutes { get; set; }
    public int RoundingMinutes { get; set; }
    public OvertimeRoundingMode RoundingMode { get; set; } = OvertimeRoundingMode.None;
    public int? MaximumMinutesPerDay { get; set; }
    public int? MaximumMinutesPerMonth { get; set; }
    public bool RequirePreApproval { get; set; }
    public bool RequirePostApproval { get; set; } = true;
    public bool AllowNormalWorkingDay { get; set; } = true;
    public bool AllowWeekOff { get; set; }
    public bool AllowHoliday { get; set; }
    public decimal NormalDayMultiplier { get; set; } = 1m;
    public decimal WeekOffMultiplier { get; set; } = 1m;
    public decimal HolidayMultiplier { get; set; } = 1m;
    public string EligibilityMode { get; set; } = "All";
    public Guid? HourlyRateComponentId { get; set; }
    public int? MonthlyWorkMinutes { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
}

public sealed class OvertimeRequest : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? EmploymentId { get; set; }
    public DateOnly WorkDate { get; set; }
    public int RequestedMinutes { get; set; }
    public int ActualEligibleMinutes { get; set; }
    public int ApprovedMinutes { get; set; }
    public OvertimeCategory Category { get; set; }
    public string? Reason { get; set; }
    public Guid PolicyId { get; set; }
    public OvertimeRequestStatus Status { get; set; } = OvertimeRequestStatus.Draft;
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public string? CorrectionReason { get; set; }
    public Guid? CorrectedByUserId { get; set; }
    public DateTime? CorrectedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public OvertimePolicy? Policy { get; set; }
}

public sealed class EmployeeMonthlyOvertime : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendancePeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? EmploymentId { get; set; }
    public int Version { get; set; }
    public int NormalDayMinutes { get; set; }
    public int WeekOffMinutes { get; set; }
    public int HolidayMinutes { get; set; }
    public int TotalApprovedMinutes { get; set; }
    public Guid AttendanceSnapshotId { get; set; }
    public int AttendanceVersion { get; set; }
    public Guid? PolicyId { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsFinalized { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public AttendancePeriod? AttendancePeriod { get; set; }
    public Employee? Employee { get; set; }
    public PayrollAttendanceSnapshot? AttendanceSnapshot { get; set; }
}

public sealed class PayrollOvertimeSnapshot : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendancePeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? EmploymentId { get; set; }
    public int Version { get; set; }
    public bool IsCurrent { get; set; }
    public Guid AttendanceSnapshotId { get; set; }
    public int AttendanceVersion { get; set; }
    public int NormalDayMinutes { get; set; }
    public int WeekOffMinutes { get; set; }
    public int HolidayMinutes { get; set; }
    public int TotalApprovedMinutes { get; set; }
    public decimal NormalDayMultiplier { get; set; }
    public decimal WeekOffMultiplier { get; set; }
    public decimal HolidayMultiplier { get; set; }
    public Guid? PolicyId { get; set; }
    public Guid? HourlyRateComponentId { get; set; }
    public int? MonthlyWorkMinutes { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public DateTime FinalizedAtUtc { get; set; }
    public string? ReopenReason { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public DateTime? ReopenedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public AttendancePeriod? AttendancePeriod { get; set; }
    public Employee? Employee { get; set; }
}
