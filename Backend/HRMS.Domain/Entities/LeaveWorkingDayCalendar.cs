using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

public sealed class Holiday : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public Guid? CountryLocationId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }

    public Tenant? Tenant { get; set; }
    public Country? CountryLocation { get; set; }
    public WorkLocation? WorkLocation { get; set; }
}

public sealed class WeeklyOffConfiguration : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public Guid? CountryLocationId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }

    public Tenant? Tenant { get; set; }
    public Country? CountryLocation { get; set; }
    public WorkLocation? WorkLocation { get; set; }
    public ICollection<WeeklyOffDay> Days { get; set; } = new List<WeeklyOffDay>();
}

public sealed class WeeklyOffDay : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WeeklyOffConfigurationId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }

    public Tenant? Tenant { get; set; }
    public WeeklyOffConfiguration? WeeklyOffConfiguration { get; set; }
}
