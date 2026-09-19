using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>Tenant-scoped definition of a payroll component. Amounts belong to future salary assignments.</summary>
public class SalaryComponent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SalaryComponentType ComponentType { get; set; }
    public SalaryCalculationType CalculationType { get; set; }
    public SalaryStatutoryType StatutoryType { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsStatutory { get; set; }
    public bool IsRecurring { get; set; }
    public bool AffectsGross { get; set; }
    public bool AffectsNetPay { get; set; }
    public int DisplayOrder { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public int ConcurrencyVersion { get; set; } = 1;

    public Tenant? Tenant { get; set; }
    public ICollection<SalaryComponentHistory> History { get; set; } = new List<SalaryComponentHistory>();
}

public class SalaryComponentHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SalaryComponentId { get; set; }
    public Guid? ActorUserId { get; set; }
    public SalaryComponentChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SalaryComponentType ComponentType { get; set; }
    public SalaryCalculationType CalculationType { get; set; }
    public SalaryStatutoryType StatutoryType { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsStatutory { get; set; }
    public bool IsRecurring { get; set; }
    public bool AffectsGross { get; set; }
    public bool AffectsNetPay { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }

    public Tenant? Tenant { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
    public User? ActorUser { get; set; }
}
