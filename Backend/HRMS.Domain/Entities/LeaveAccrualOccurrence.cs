using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>Durable processing state for one policy accrual interval.</summary>
public sealed class LeaveAccrualOccurrence : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public Guid LeavePeriodId { get; set; }
    public Guid LeavePolicyVersionId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public AccrualFrequency AccrualFrequency { get; set; }
    public DateOnly OccurrenceDate { get; set; }
    public string OccurrenceKey { get; set; } = string.Empty;
    public decimal CalculatedQuantity { get; set; }
    public decimal CreditedQuantity { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? ClaimToken { get; set; }

    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeavePeriod? LeavePeriod { get; set; }
    public LeavePolicyVersion? LeavePolicyVersion { get; set; }
    public LeavePolicyRule? LeavePolicyRule { get; set; }
}
