using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class SalaryStructureQuery : PagedQuery
{
    public bool? IsActive { get; set; }
    public DateOnly? EffectiveOn { get; set; }
}

public sealed class SalaryStructureComponentRequest
{
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
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class SalaryStructureRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SalaryStructureComponentRequest> Components { get; set; } = [];
    public int? ExpectedConcurrencyVersion { get; set; }
}

public sealed record SalaryStructureComponentDto(
    Guid Id, Guid SalaryComponentId, string SalaryComponentCode, string SalaryComponentName,
    SalaryComponentType ComponentType, int Sequence, SalaryStructureCalculationType CalculationType,
    decimal? Value, Guid? PercentageOfComponentId, string? Formula, bool IsProratable,
    bool IsEditableAtEmployeeLevel, decimal? MinimumAmount, decimal? MaximumAmount, bool IsActive,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record SalaryStructureDto(
    Guid Id, Guid VersionId, string Code, string Name, string? Description,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, int ComponentCount,
    int ConcurrencyVersion, IReadOnlyList<SalaryStructureComponentDto> Components,
    DateTime CreatedDate, DateTime? ModifiedDate);

public sealed record SalaryStructureHistoryDto(
    Guid Id, Guid SalaryStructureId, Guid? SalaryStructureVersionId,
    SalaryStructureChangeType ChangeType, string Code, string Name, string? Description,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string ComponentsJson,
    Guid? ActorUserId, DateTime ChangedAtUtc);
