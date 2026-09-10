using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>Source and expiry metadata reconciled to one positive ledger credit.</summary>
public sealed class LeaveEntitlementGrant : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeLeaveBalanceId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public Guid LeavePeriodId { get; set; }
    public Guid? LeaveBalanceTransactionId { get; set; }
    public LeaveBalanceSourceType SourceType { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public decimal GrantedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ExpiredQuantity { get; set; }
    public DateOnly GrantedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string Status { get; set; } = "Active";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal AvailableQuantity => GrantedQuantity - ReservedQuantity - ConsumedQuantity - ExpiredQuantity;
}
