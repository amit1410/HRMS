using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// A full employee record. Department, designation and manager are returned as id plus display name so a
/// client can render the record without three follow-up requests, while still having the ids it needs to
/// pre-select values in an edit form.
/// </summary>
public record EmployeeDto(
    Guid Id,
    string EmployeeCode,
    string? Salutation,
    string FirstName,
    string? MiddleName,
    string LastName,
    string FullName,
    string Email,
    string? Phone,
    DateOnly? DateOfBirth,
    Gender Gender,
    BloodGroup BloodGroup,
    MaritalStatus MaritalStatus,
    string? BirthCountry,
    string? BirthState,
    string? BirthCity,
    Guid? BirthCountryId,
    string? BirthCountryName,
    Guid? BirthStateId,
    string? BirthStateName,
    Guid? BirthCityId,
    string? BirthCityName,
    string? Religion,
    string? Caste,
    DateOnly DateOfJoining,
    DateOnly? GroupDateOfJoining,
    DateOnly? DateOfLeaving,
    EmployeeStatus Status,
    string? JobStatus,
    string? GroupId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? DesignationId,
    string? DesignationName,
    Guid? ReportingManagerId,
    string? ReportingManagerName,
    string? EmployeeType,
    string? MaskedAadhaarNumber,
    string? MaskedPanNumber,
    string? MaskedPfNumber,
    string? MaskedUanNumber,
    string? MaskedEsicNumber,
    string? MaskedMediclaimNumber,
    bool Gratuity,
    bool Pension,
    string? CostCenterCode,
    string? PayrollLocation,
    bool EsicApplicable,
    string? Citizenship,
    string? LanguageKnown,
    string? ProfilePictureUrl,
    string? Address,
    DateTime CreatedDate,
    DateTime? ModifiedDate)
{
    /// <summary>Masks an Aadhaar number as "XXXX-XXXX-1234".</summary>
    public static string? MaskAadhaar(string? value) => Common.SensitiveDataMasker.Aadhaar(value);

    /// <summary>Masks a PAN number as "A****F".</summary>
    public static string? MaskPan(string? value) => Common.SensitiveDataMasker.Pan(value);

    /// <summary>Masks a PF/UAN number as "******1234" (last 4 digits visible).</summary>
    public static string? MaskNumericId(string? value) => Common.SensitiveDataMasker.Identifier(value);
}
