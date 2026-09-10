namespace HRMS.Application.DTOs.Leave;

public sealed record HolidayQuery(DateOnly? From = null, DateOnly? To = null, Guid? CountryId = null, Guid? WorkLocationId = null, bool? IsActive = true);

public sealed record HolidayDto(Guid Id, string Name, DateOnly Date, Guid? CountryLocationId, Guid? WorkLocationId, bool IsActive, DateTime CreatedDate, DateTime? ModifiedDate, string ConcurrencyToken);

public sealed record HolidayRequest(string Name, DateOnly Date, Guid? CountryLocationId, Guid? WorkLocationId, bool IsActive = true, string? ConcurrencyToken = null);

public sealed record WeeklyOffConfigurationDto(Guid Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid? CountryLocationId, Guid? WorkLocationId, IReadOnlyList<DayOfWeek> Days, bool IsActive, DateTime CreatedDate, DateTime? ModifiedDate, string ConcurrencyToken);

public sealed record WeeklyOffConfigurationRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid? CountryLocationId, Guid? WorkLocationId, IReadOnlyList<DayOfWeek> Days, bool IsActive = true, string? ConcurrencyToken = null);
