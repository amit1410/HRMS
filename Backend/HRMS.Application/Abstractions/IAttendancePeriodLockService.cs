using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public interface IAttendancePeriodLockService
{
    Task<Result<bool>> EnsureDateIsOpenAsync(DateOnly date, CancellationToken ct = default);
    Task<Result<bool>> EnsureRangeIsOpenAsync(DateOnly startDate, DateOnly endDate, CancellationToken ct = default);
}
