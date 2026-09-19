using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class EmployeeSalaryAssignmentQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public Guid? SalaryStructureId { get; set; }
    public EmployeeSalaryAssignmentStatus? Status { get; set; }
    public DateOnly? EffectiveOn { get; set; }
}

public sealed class EmployeeSalaryComponentRequest
{
    public Guid SalaryStructureComponentId { get; set; }
    public decimal? OverrideValue { get; set; }
    public decimal? OverridePercentage { get; set; }
    public string? OverrideFormula { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Remarks { get; set; }
}

public sealed class EmployeeSalaryAssignmentRequest
{
    public Guid EmployeeId { get; set; }
    public Guid SalaryStructureId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal? AnnualCtc { get; set; }
    public decimal? MonthlyCtc { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public SalaryPayFrequency PayFrequency { get; set; } = SalaryPayFrequency.Monthly;
    public EmployeeSalaryAssignmentStatus Status { get; set; } = EmployeeSalaryAssignmentStatus.Active;
    public SalaryChangeReason ChangeReason { get; set; } = SalaryChangeReason.Other;
    public string? Remarks { get; set; }
    public int? ExpectedConcurrencyVersion { get; set; }
    public List<EmployeeSalaryComponentRequest> Components { get; set; } = [];
}

public sealed record EmployeeSalaryComponentDto(Guid Id, Guid SalaryStructureComponentId, Guid SalaryComponentId, string SalaryComponentCode, string SalaryComponentName, SalaryStructureCalculationType CalculationType, bool IsEditableAtEmployeeLevel, decimal? OverrideValue, decimal? OverridePercentage, string? OverrideFormula, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? Remarks);
public sealed record EmployeeSalaryAssignmentDto(Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName, Guid SalaryStructureId, string SalaryStructureCode, string SalaryStructureName, Guid SalaryStructureVersionId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal? AnnualCtc, decimal? MonthlyCtc, string CurrencyCode, SalaryPayFrequency PayFrequency, EmployeeSalaryAssignmentStatus Status, SalaryChangeReason ChangeReason, string? Remarks, int ConcurrencyVersion, IReadOnlyList<EmployeeSalaryComponentDto> Components);
public sealed record EmployeeSalaryAssignmentHistoryDto(Guid Id, Guid AssignmentId, EmployeeSalaryAssignmentChangeType ChangeType, Guid EmployeeId, Guid SalaryStructureId, Guid SalaryStructureVersionId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal? AnnualCtc, decimal? MonthlyCtc, string CurrencyCode, SalaryPayFrequency PayFrequency, EmployeeSalaryAssignmentStatus Status, SalaryChangeReason ChangeReason, string? Remarks, string ComponentsJson, DateTime ChangedAtUtc, Guid? ActorUserId);
