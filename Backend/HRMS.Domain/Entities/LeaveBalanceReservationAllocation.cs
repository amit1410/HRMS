using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

public sealed class LeaveBalanceReservationAllocation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid LeaveEntitlementGrantId { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public string Status { get; set; } = "Reserved";
    public Tenant? Tenant { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
    public LeaveEntitlementGrant? LeaveEntitlementGrant { get; set; }
}
