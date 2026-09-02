namespace HRMS.Application.DTOs.Employees;

public class EmployeeContactRequest
{
    public string? OfficialEmail { get; set; }
    public string? PersonalEmail { get; set; }
    public string? AlternateEmail { get; set; }
    public string? OfficialPhone { get; set; }
    public string? PersonalPhone { get; set; }
    public string? EmergencyNumber { get; set; }
    public bool SameAsCurrentAddress { get; set; }
}
