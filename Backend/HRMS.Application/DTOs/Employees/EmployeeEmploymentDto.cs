using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Read DTO for an employee's joining information and contractual terms.
/// Backed by the <c>EmployeeEmployments</c> table (1:1 with Employee).
/// </summary>
public record EmployeeEmploymentDto(
    Guid Id,
    Guid EmployeeId,
    DateOnly? ConfirmationDate,
    string? JobStatus,
    Guid? ReferredByEmployeeId,
    string? ReferredByEmployeeName,
    int? NoticePeriod,
    string? NoticePeriodUnit,
    DateOnly? NoticeStartDate,
    DateOnly? NoticeEndDate,
    NoticePeriodStatus NoticeStatus,
    DateTime CreatedDate,
    DateTime? ModifiedDate,
    // Confirmation & probation policy integration (Phase 9a). When an applicable confirmation policy is
    // resolved, the probation end is policy-derived and ConfirmationDate becomes read-only in the UI; when
    // no policy applies these are false/null and the manual ConfirmationDate behaves exactly as before.
    bool ConfirmationDateIsPolicyManaged = false,
    DateOnly? PolicyProbationEndDate = null,
    string? ConfirmationPolicyName = null);
