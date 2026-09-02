using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public class EmployeeCodeRuleCondition : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeCodeRuleId { get; set; }
    public EmployeeCodeConditionField Field { get; set; }
    public EmployeeCodeConditionOperator Operator { get; set; } = EmployeeCodeConditionOperator.Equals;
    public Guid? ReferenceId { get; set; }
    public string? Value { get; set; }

    public Tenant? Tenant { get; set; }
    public EmployeeCodeRule? Rule { get; set; }
}
