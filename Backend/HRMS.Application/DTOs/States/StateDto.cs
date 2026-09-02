namespace HRMS.Application.DTOs.States;

public record StateDto(
    Guid Id,
    Guid CountryId,
    string CountryName,
    string Code,
    string Name,
    bool IsActive,
    int CityCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
