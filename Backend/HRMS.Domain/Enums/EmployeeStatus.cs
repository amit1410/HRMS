namespace HRMS.Domain.Enums;

/// <summary>
/// Employment lifecycle state. This — not deletion — is how an employee who has left the organization is
/// represented: HR records must survive the person leaving, so the row stays and its status changes.
/// </summary>
public enum EmployeeStatus
{
    Active = 1,
    Resigned = 2,
    Terminated = 3
}
