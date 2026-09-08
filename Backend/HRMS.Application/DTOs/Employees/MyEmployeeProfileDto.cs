using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public sealed record MyEmployeeProfileDto(
    string? EmployeeCode,
    string? Salutation,
    string FirstName,
    string? MiddleName,
    string LastName,
    string FullName,
    Gender Gender,
    DateOnly? DateOfBirth,
    BloodGroup BloodGroup,
    MaritalStatus MaritalStatus,
    string? Citizenship,
    string? BirthCountry,
    string? BirthState,
    string? BirthCity,
    string? Religion,
    string? Caste,
    string? MaskedAadhaar,
    string? MaskedPan,
    string? MaskedUan,
    string? MaskedPf,
    string? MaskedEsic,
    string? MediclaimNumber,
    bool EsicApplicable,
    bool Gratuity,
    bool Pension,
    DateOnly DateOfJoining,
    DateOnly? GroupDateOfJoining,
    string? EmployeeType,
    string? JobStatus,
    EmployeeStatus Status,
    string? GroupId,
    string? PayrollLocation,
    string? CostCenterCode,
    string? ProfilePictureUrl,
    MyEmployeeContactDto Contact,
    MyEmployeeAddressDto? CurrentAddress,
    MyEmployeeAddressDto? PermanentAddress,
    MyCurrentEmploymentDto? CurrentEmployment,
    IReadOnlyList<MyEmployeeBankDto> BankDetails);

public sealed record MyEmployeeContactDto(string? Email, string? Phone);

public sealed record MyEmployeeAddressDto(
    string? Country, string? State, string? District, string? City, string? ZipCode,
    string? AddressLine1, string? AddressLine2, string? HouseNumber);

public sealed record MyCurrentEmploymentDto(
    string? HoldingCompany, string? Lob, string? Organization, string? Department,
    string? SubDepartment, string? Section, string? SubSection, string? Function,
    string? SubFunction, string? Grade, string? Designation, string? EmployeeType,
    string? Country, string? WorkLocation, string? CostCenter,
    DateOnly EffectiveFrom, string? ReportingManager, EmploymentType EmploymentType,
    EmployeeStatus EmploymentStatus);

public sealed record MyEmployeeBankDto(
    string BankName, string MaskedAccountNumber, string? MaskedIfsc, string? Branch,
    AccountType AccountType, DateOnly? EffectiveFrom);
