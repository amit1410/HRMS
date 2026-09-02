using HRMS.Domain.Enums;

namespace HRMS.Application.EmployeeCodes;

public sealed record EmployeeCodeGenerationContext(
    DateOnly JoiningDate,
    IReadOnlyDictionary<EmployeeCodeConditionField, string?> Values,
    IReadOnlyDictionary<EmployeeCodeSegmentType, string?> SegmentValues);
