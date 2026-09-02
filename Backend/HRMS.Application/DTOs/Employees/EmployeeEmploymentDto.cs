namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Read DTO for an employee's joining information and contractual terms.
/// Backed by the <c>EmployeeEmployments</c> table (1:1 with Employee).
/// </summary>
public record EmployeeEmploymentDto(
    Guid Id,
    Guid EmployeeId,
    DateOnly FirstHiredDate,
    DateOnly DateOfJoining,
    DateOnly? GroupDateOfJoining,
    DateOnly? ConfirmationDate,
    string? JobStatus,
    int? ProbationPeriod,
    string? ProbationPeriodUnit,
    Guid? ReferredByEmployeeId,
    string? ReferredByEmployeeName,
    int? NoticePeriod,
    string? NoticePeriodUnit,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
