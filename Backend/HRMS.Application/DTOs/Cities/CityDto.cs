namespace HRMS.Application.DTOs.Cities;

public record CityDto(
    Guid Id,
    Guid StateId,
    string StateName,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
