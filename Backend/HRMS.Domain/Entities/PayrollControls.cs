using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

public sealed class PayrollControlConfiguration : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public bool RequireMakerChecker { get; set; } = true;
    public bool PreventSelfApproval { get; set; } = true;
    public bool RequireReasonForReopen { get; set; } = true;
    public bool RequireReasonForCancellation { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
}
