using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Request DTO for creating or updating an employee's joining information.
/// </summary>
public class EmployeeEmploymentRequest
{
    public DateOnly? ConfirmationDate { get; set; }
    public string? JobStatus { get; set; }
    public Guid? ReferredByEmployeeId { get; set; }
    public int? NoticePeriod { get; set; }
    public string? NoticePeriodUnit { get; set; }
    public DateOnly? NoticeStartDate { get; set; }
    public DateOnly? NoticeEndDate { get; set; }
    public NoticePeriodStatus NoticeStatus { get; set; } = NoticePeriodStatus.NotServing;
}
