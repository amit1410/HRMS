using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public interface IWorkingDayCalendarResolver
{
    Task<Result<WorkingDayCalculationResult>> CalculateAsync(
        Guid employeeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}

public sealed record WorkingDayCalculationResult(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<WorkingDayDetail> Days)
{
    public int TotalCalendarDays => Days.Count;
    public decimal WorkingLeaveDays => Days.Count(x => x.IsWorkingDay);
    public int ExcludedHolidayCount => Days.Count(x => x.ExclusionReason == WorkingDayExclusionReason.Holiday);
    public int ExcludedWeeklyOffCount => Days.Count(x => x.ExclusionReason == WorkingDayExclusionReason.WeeklyOff);
}

public sealed record WorkingDayDetail(
    DateOnly Date,
    bool IsWorkingDay,
    WorkingDayExclusionReason? ExclusionReason,
    string? HolidayName);

public enum WorkingDayExclusionReason
{
    Holiday,
    WeeklyOff
}
