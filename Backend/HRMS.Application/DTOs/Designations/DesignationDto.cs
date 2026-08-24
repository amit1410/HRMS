namespace HRMS.Application.DTOs.Designations;

/// <summary>A job title as returned to clients, with the number of employees currently holding it.</summary>
public record DesignationDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int EmployeeCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
