namespace HRMS.Application.DTOs.Countries;

public record CountryDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    int StateCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
