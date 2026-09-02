using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public class EmployeeCodeRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeCodeConfigId { get; set; }
    public Guid? EmployeeCodeConfigVersionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsDefault { get; set; }
    public EmployeeCodeRuleStatus Status { get; set; } = EmployeeCodeRuleStatus.Draft;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public EmployeeCodeConfig? Configuration { get; set; }
    public EmployeeCodeConfigVersion? ConfigurationVersion { get; set; }
    public ICollection<EmployeeCodeRuleCondition> Conditions { get; set; } = new List<EmployeeCodeRuleCondition>();
    public ICollection<EmployeeCodeSegment> Segments { get; set; } = new List<EmployeeCodeSegment>();
}
