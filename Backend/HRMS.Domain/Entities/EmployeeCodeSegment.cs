using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public class EmployeeCodeSegment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeCodeRuleId { get; set; }
    public int SequenceOrder { get; set; }
    public EmployeeCodeSegmentType SegmentType { get; set; }
    public string? FixedValue { get; set; }
    public int? PaddingLength { get; set; }

    public Tenant? Tenant { get; set; }
    public EmployeeCodeRule? Rule { get; set; }
}
