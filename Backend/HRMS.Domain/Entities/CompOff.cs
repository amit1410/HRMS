using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class CompOffPolicy : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool AllowWeekOff { get; set; } = true;
    public bool AllowHoliday { get; set; } = true;
    public bool AllowOvertimeSource { get; set; }
    public string EligibilityMode { get; set; } = "All";
    public int MinimumWorkedMinutes { get; set; }
    public decimal CreditRatio { get; set; } = 1m;
    public CompOffRoundingMode RoundingMode { get; set; } = CompOffRoundingMode.None;
    public int RoundingMinutes { get; set; }
    public int? MaximumCreditMinutesPerDay { get; set; }
    public int? MaximumCreditMinutesPerMonth { get; set; }
    public int? ExpiryDays { get; set; }
    public int? ExpiryMonths { get; set; }
    public bool RequireCreditApproval { get; set; }
    public bool AllowPartialDayConsumption { get; set; } = true;
    public int ConsumptionIncrementMinutes { get; set; } = 1;
    public CompOffBenefitMode BenefitMode { get; set; } = CompOffBenefitMode.CompOffOnly;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
}

public sealed class CompOffEarning : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? EmploymentId { get; set; }
    public DateOnly SourceWorkDate { get; set; }
    public CompOffSourceType SourceType { get; set; }
    public Guid? SourceAttendanceDayId { get; set; }
    public Guid? SourceAttendanceSnapshotId { get; set; }
    public int SourceAttendanceVersion { get; set; }
    public Guid? SourceOvertimeRequestId { get; set; }
    public Guid? SourceOvertimeSnapshotId { get; set; }
    public Guid PolicyId { get; set; }
    public int PolicyVersion { get; set; }
    public int SourceWorkedMinutes { get; set; }
    public int EligibleMinutes { get; set; }
    public int CreditedMinutes { get; set; }
    public CompOffEarningStatus Status { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? ApprovedByEmployeeId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public CompOffPolicy? Policy { get; set; }
    public ICollection<CompOffLedgerEntry> LedgerEntries { get; set; } = new List<CompOffLedgerEntry>();
}

public sealed class CompOffLedgerEntry : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid EarningId { get; set; }
    public Guid? LeaveRequestId { get; set; }
    public CompOffLedgerEntryType EntryType { get; set; }
    public int Minutes { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorEmployeeId { get; set; }
    public Tenant? Tenant { get; set; }
    public CompOffEarning? Earning { get; set; }
}

public sealed class CompOffLeaveAllocation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid EarningId { get; set; }
    public int ReservedMinutes { get; set; }
    public int ConsumedMinutes { get; set; }
    public int ReleasedMinutes { get; set; }
    public string Status { get; set; } = "Reserved";
    public Tenant? Tenant { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
    public CompOffEarning? Earning { get; set; }
}
