namespace HRMS.Domain.Enums;

/// <summary>
/// Blood group recorded on an employee or family member record.
/// <see cref="Unspecified"/> is the default so the field is never forced.
/// </summary>
public enum BloodGroup
{
    Unspecified = 0,
    APositive = 1,
    ANegative = 2,
    BPositive = 3,
    BNegative = 4,
    OPositive = 5,
    ONegative = 6,
    ABPositive = 7,
    ABNegative = 8
}
