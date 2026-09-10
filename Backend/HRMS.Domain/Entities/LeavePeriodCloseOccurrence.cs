using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

public sealed class LeavePeriodCloseOccurrence : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public Guid SourceLeavePeriodId { get; set; }
    public Guid DestinationLeavePeriodId { get; set; }
    public Guid LeavePolicyVersionId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public string OccurrenceKey { get; set; } = string.Empty;
    public decimal ClosingQuantity { get; set; }
    public decimal CarriedQuantity { get; set; }
    public decimal LapsedQuantity { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? ClaimToken { get; set; }
}
