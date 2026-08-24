namespace HRMS.Domain.Enums;

/// <summary>
/// Gender recorded on an employee record. <see cref="Unspecified"/> is the default so the field is never
/// forced: it is optional demographic data, and an empty value must be representable without guessing.
/// </summary>
public enum Gender
{
    Unspecified = 0,
    Male = 1,
    Female = 2,
    Other = 3
}
