using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public class EmployeeCodeSequence : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeCodeRuleId { get; set; }
    public EmployeeCodeSequenceScope Scope { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public long NextNumber { get; set; } = 1;
    public int IncrementBy { get; set; } = 1;
    public EmployeeCodeResetPeriod ResetPeriod { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = [];

    public Tenant? Tenant { get; set; }
    public EmployeeCodeRule? Rule { get; set; }
}
