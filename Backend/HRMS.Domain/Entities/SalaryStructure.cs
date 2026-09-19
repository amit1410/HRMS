using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>Reusable tenant-scoped salary structure identity. Effective configuration lives in versions.</summary>
public sealed class SalaryStructure : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int ConcurrencyVersion { get; set; } = 1;

    public Tenant? Tenant { get; set; }
    public ICollection<SalaryStructureVersion> Versions { get; set; } = new List<SalaryStructureVersion>();
    public ICollection<SalaryStructureHistory> History { get; set; } = new List<SalaryStructureHistory>();
}

/// <summary>An immutable effective-dated configuration version of a salary structure.</summary>
public sealed class SalaryStructureVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SalaryStructureId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public int ConcurrencyVersion { get; set; } = 1;

    public Tenant? Tenant { get; set; }
    public SalaryStructure? SalaryStructure { get; set; }
    public ICollection<SalaryStructureComponent> Components { get; set; } = new List<SalaryStructureComponent>();
    public ICollection<SalaryStructureHistory> History { get; set; } = new List<SalaryStructureHistory>();
}

/// <summary>Component configuration within one effective salary structure version.</summary>
public sealed class SalaryStructureComponent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SalaryStructureVersionId { get; set; }
    public Guid SalaryComponentId { get; set; }
    public int Sequence { get; set; }
    public SalaryStructureCalculationType CalculationType { get; set; }
    public decimal? Value { get; set; }
    public Guid? PercentageOfComponentId { get; set; }
    public string? Formula { get; set; }
    public bool IsProratable { get; set; }
    public bool IsEditableAtEmployeeLevel { get; set; }
    public decimal? MinimumAmount { get; set; }
    public decimal? MaximumAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public Tenant? Tenant { get; set; }
    public SalaryStructureVersion? SalaryStructureVersion { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
    public SalaryComponent? PercentageOfComponent { get; set; }
}

/// <summary>Immutable header and component snapshot for salary structure audit.</summary>
public sealed class SalaryStructureHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SalaryStructureId { get; set; }
    public Guid? SalaryStructureVersionId { get; set; }
    public Guid? ActorUserId { get; set; }
    public SalaryStructureChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string ComponentsJson { get; set; } = "[]";

    public Tenant? Tenant { get; set; }
    public SalaryStructure? SalaryStructure { get; set; }
    public SalaryStructureVersion? SalaryStructureVersion { get; set; }
    public User? ActorUser { get; set; }
}
