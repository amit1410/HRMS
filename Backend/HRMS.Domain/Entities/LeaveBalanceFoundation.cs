using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// Rebuildable current projection for one finite Employee/LeaveType/LeavePeriod balance.
/// The immutable LeaveBalanceTransaction history is the source of truth.
/// </summary>
public sealed class EmployeeLeaveBalance : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public Guid LeavePeriodId { get; set; }
    public decimal GrantedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeavePeriod? LeavePeriod { get; set; }
    public ICollection<LeaveBalanceTransaction> Transactions { get; set; } = new List<LeaveBalanceTransaction>();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal AvailableQuantity => GrantedQuantity - ReservedQuantity - ConsumedQuantity;
}

/// <summary>Append-only business history for finite balance projection changes.</summary>
public sealed class LeaveBalanceTransaction : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeLeaveBalanceId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public Guid LeavePeriodId { get; set; }
    public Guid? LeaveRequestId { get; set; }
    public LeaveBalanceTransactionType TransactionType { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid? LeavePolicyVersionId { get; set; }
    public Guid? LeavePolicyRuleId { get; set; }
    public LeaveBalanceSourceType SourceType { get; set; }
    public string? SourceReference { get; set; }
    public LeaveBalanceActorType ActorType { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorEmployeeId { get; set; }
    public string? CorrelationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadFingerprint { get; set; } = string.Empty;

    public Tenant? Tenant { get; set; }
    public EmployeeLeaveBalance? EmployeeLeaveBalance { get; set; }
    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeavePeriod? LeavePeriod { get; set; }
    public LeavePolicyVersion? LeavePolicyVersion { get; set; }
    public LeavePolicyRule? LeavePolicyRule { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
}
