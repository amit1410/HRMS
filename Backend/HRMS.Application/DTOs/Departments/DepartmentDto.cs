namespace HRMS.Application.DTOs.Departments;

/// <summary>
/// A department as returned to clients. <see cref="EmployeeCount"/> is included because it is what tells a
/// caller whether the department can be deleted, saving a second round trip to find out.
/// </summary>
public record DepartmentDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int EmployeeCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
