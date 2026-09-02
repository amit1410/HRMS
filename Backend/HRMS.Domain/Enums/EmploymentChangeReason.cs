namespace HRMS.Domain.Enums;

/// <summary>
/// Reason for an employment change. Used in employment history records to document why
/// an effective-dated change was made.
/// </summary>
public enum EmploymentChangeReason
{
    Unspecified = 0,
    NewJoining = 1,
    Promotion = 2,
    Transfer = 3,
    DepartmentChange = 4,
    RoleChange = 5,
    LocationChange = 6,
    GradeChange = 7,
    ManagerChange = 8,
    OrganizationalRestructure = 9,
    Correction = 10,
    Other = 99
}
