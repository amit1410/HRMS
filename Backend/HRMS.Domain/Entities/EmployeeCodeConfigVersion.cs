using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public class EmployeeCodeConfigVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeCodeConfigId { get; set; }
    public bool AutoGenerate { get; set; }
    public EmployeeCodeAssignmentMode AssignmentMode { get; set; } = EmployeeCodeAssignmentMode.Auto;
    public EmployeeCodeGenerationMethod? GenerationMethod { get; set; } = EmployeeCodeGenerationMethod.Simple;
    public string Prefix { get; set; } = "EMP";
    public string Separator { get; set; } = "-";
    public long NextNumber { get; set; } = 1;
    public int Padding { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public EmployeeCodeConfig? Configuration { get; set; }
    public ICollection<EmployeeCodeRule> Rules { get; set; } = new List<EmployeeCodeRule>();
}
