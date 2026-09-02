namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Request DTO for creating or updating an employee's joining information.
/// </summary>
public class EmployeeEmploymentRequest
{
    public DateOnly FirstHiredDate { get; set; }
    public DateOnly DateOfJoining { get; set; }
    public DateOnly? GroupDateOfJoining { get; set; }
    public DateOnly? ConfirmationDate { get; set; }
    public string? JobStatus { get; set; }
    public int? ProbationPeriod { get; set; }
    public string? ProbationPeriodUnit { get; set; }
    public Guid? ReferredByEmployeeId { get; set; }
    public int? NoticePeriod { get; set; }
    public string? NoticePeriodUnit { get; set; }
}
