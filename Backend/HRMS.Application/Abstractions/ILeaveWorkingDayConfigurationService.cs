using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;

namespace HRMS.Application.Abstractions;

public interface ILeaveWorkingDayConfigurationService
{
    Task<Result<IReadOnlyList<HolidayDto>>> GetHolidaysAsync(HolidayQuery query, CancellationToken cancellationToken = default);
    Task<Result<HolidayDto>> CreateHolidayAsync(HolidayRequest request, CancellationToken cancellationToken = default);
    Task<Result<HolidayDto>> UpdateHolidayAsync(Guid id, HolidayRequest request, CancellationToken cancellationToken = default);
    Task<Result<HolidayDto>> DeactivateHolidayAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<WeeklyOffConfigurationDto>>> GetWeeklyOffConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<Result<WeeklyOffConfigurationDto>> CreateWeeklyOffConfigurationAsync(WeeklyOffConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<Result<WeeklyOffConfigurationDto>> UpdateWeeklyOffConfigurationAsync(Guid id, WeeklyOffConfigurationRequest request, CancellationToken cancellationToken = default);
}
