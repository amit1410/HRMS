namespace HRMS.Domain.Enums;

/// <summary>
/// Marital status recorded on an employee record. <see cref="Unspecified"/> is the default
/// so the field is never forced — it is optional demographic data.
/// </summary>
public enum MaritalStatus
{
    Unspecified = 0,
    Single = 1,
    Married = 2,
    Divorced = 3,
    Widowed = 4,
    Separated = 5
}
