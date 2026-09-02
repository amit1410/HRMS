using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public sealed record EmployeeCodeSegmentDto(Guid Id, int SequenceOrder, EmployeeCodeSegmentType SegmentType, string? FixedValue, int? PaddingLength);
public sealed record EmployeeCodeConditionDto(Guid Id, EmployeeCodeConditionField Field, EmployeeCodeConditionOperator Operator, Guid? ReferenceId, string? Value);
public sealed record EmployeeCodeRuleDto(Guid Id, string Name, int Priority, bool IsDefault, EmployeeCodeRuleStatus Status, IReadOnlyList<EmployeeCodeConditionDto> Conditions, IReadOnlyList<EmployeeCodeSegmentDto> Segments, Guid? ConfigurationVersionId = null);

public sealed class EmployeeCodeRuleRequest
{
    public Guid? ConfigurationVersionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsDefault { get; set; }
    public EmployeeCodeRuleStatus Status { get; set; } = EmployeeCodeRuleStatus.Draft;
    public List<EmployeeCodeConditionRequest> Conditions { get; set; } = [];
    public List<EmployeeCodeSegmentRequest> Segments { get; set; } = [];
}

public sealed class EmployeeCodeConditionRequest
{
    public Guid? Id { get; set; }
    public EmployeeCodeConditionField Field { get; set; }
    public EmployeeCodeConditionOperator Operator { get; set; } = EmployeeCodeConditionOperator.Equals;
    public Guid? ReferenceId { get; set; }
    public string? Value { get; set; }
}

public sealed class EmployeeCodeSegmentRequest
{
    public Guid? Id { get; set; }
    public int SequenceOrder { get; set; }
    public EmployeeCodeSegmentType SegmentType { get; set; }
    public string? FixedValue { get; set; }
    public int? PaddingLength { get; set; }
}
