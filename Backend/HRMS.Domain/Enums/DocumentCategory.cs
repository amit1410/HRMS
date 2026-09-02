namespace HRMS.Domain.Enums;

/// <summary>
/// Category of an employee document.
/// </summary>
public enum DocumentCategory
{
    Unspecified = 0,
    Identity = 1,
    Address = 2,
    Education = 3,
    Experience = 4,
    Salary = 5,
    OfferLetter = 6,
    AppointmentLetter = 7,
    RelievingLetter = 8,
    ExperienceLetter = 9,
    Photo = 10,
    Signature = 11,
    Other = 99
}
