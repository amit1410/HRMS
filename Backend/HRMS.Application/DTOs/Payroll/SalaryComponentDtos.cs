using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class SalaryComponentQuery : PagedQuery
{
    public SalaryComponentType? ComponentType { get; set; }
    public SalaryCalculationType? CalculationType { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsStatutory { get; set; }
}

public sealed class SalaryComponentRequest
{
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
    public int? ExpectedConcurrencyVersion { get; set; }
}

public record SalaryComponentDto(Guid Id, string Code, string Name, string? Description,
    SalaryComponentType ComponentType, SalaryCalculationType CalculationType, SalaryStatutoryType StatutoryType,
    bool IsTaxable, bool IsStatutory, bool IsRecurring, bool AffectsGross, bool AffectsNetPay,
    int DisplayOrder, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, int ConcurrencyVersion,
    DateTime CreatedDate, DateTime? ModifiedDate);

public record SalaryComponentHistoryDto(Guid Id, Guid SalaryComponentId, SalaryComponentChangeType ChangeType,
    string Code, string Name, SalaryComponentType ComponentType, SalaryCalculationType CalculationType,
    SalaryStatutoryType StatutoryType, bool IsTaxable, bool IsStatutory, bool IsRecurring, bool AffectsGross,
    bool AffectsNetPay, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, Guid? ActorUserId,
    DateTime ChangedAtUtc);
