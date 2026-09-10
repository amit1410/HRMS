using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class WorkingDayCalendarResolver : IWorkingDayCalendarResolver
{
    private const int MaximumRangeDays = 366;
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IEffectiveEmploymentResolver _employmentResolver;

    public WorkingDayCalendarResolver(IHrmsDbContext db, ITenantContext tenantContext, IEffectiveEmploymentResolver employmentResolver)
    {
        _db = db;
        _tenantContext = tenantContext;
        _employmentResolver = employmentResolver;
    }

    public async Task<Result<WorkingDayCalculationResult>> CalculateAsync(Guid employeeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<WorkingDayCalculationResult>.Unauthorized("A tenant execution context is required.");
        if (employeeId == Guid.Empty)
            return Result<WorkingDayCalculationResult>.Invalid("employeeId", "EmployeeId is required.");
        if (fromDate > toDate)
            return Result<WorkingDayCalculationResult>.Invalid("dateRange", "The calendar end date must not be before the start date.");
        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaximumRangeDays)
            return Result<WorkingDayCalculationResult>.Invalid("dateRange", "The calendar range cannot exceed 366 days.");

        var tenantId = _tenantContext.TenantId!.Value;
        var holidays = await _db.Holidays.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.Date >= fromDate && x.Date <= toDate)
            .Select(x => new HolidaySnapshot(x.Date, x.Name, x.CountryLocationId, x.WorkLocationId))
            .ToListAsync(cancellationToken);
        var weeklyOffs = await _db.WeeklyOffConfigurations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.EffectiveFrom <= toDate && (x.EffectiveTo == null || x.EffectiveTo >= fromDate))
            .Select(x => new WeeklyOffSnapshot(x.Id, x.EffectiveFrom, x.EffectiveTo, x.CountryLocationId, x.WorkLocationId,
                x.Days.Select(day => day.DayOfWeek).ToList()))
            .ToListAsync(cancellationToken);

        var details = new List<WorkingDayDetail>(toDate.DayNumber - fromDate.DayNumber + 1);
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var employment = await _employmentResolver.ResolveAsync(tenantId, employeeId, date, cancellationToken);
            if (employment.Status != EffectiveEmploymentResolutionStatus.Resolved || employment.Employment is null)
                return Result<WorkingDayCalculationResult>.Conflict(employment.Message);

            var scope = employment.Employment;
            var holidayMatches = holidays.Where(x => x.Date == date && Applies(x.CountryLocationId, x.WorkLocationId, scope.CountryLocationId, scope.WorkLocationId)).ToList();
            var (holiday, holidayAmbiguous) = SelectMostSpecificHoliday(holidayMatches);
            if (holidayAmbiguous)
                return Result<WorkingDayCalculationResult>.Conflict("Multiple holidays with the same applicability specificity apply on the selected date.");
            if (holiday is not null)
            {
                details.Add(new(date, false, WorkingDayExclusionReason.Holiday, holiday.Name));
                continue;
            }

            var weeklyMatches = weeklyOffs.Where(x => x.EffectiveFrom <= date && (x.EffectiveTo == null || date <= x.EffectiveTo) &&
                x.Days.Contains(date.DayOfWeek) && Applies(x.CountryLocationId, x.WorkLocationId, scope.CountryLocationId, scope.WorkLocationId)).ToList();
            var (weekly, weeklyAmbiguous) = SelectMostSpecificWeeklyOff(weeklyMatches);
            if (weeklyAmbiguous)
                return Result<WorkingDayCalculationResult>.Conflict("Multiple weekly-off configurations with the same applicability specificity apply on the selected date.");
            details.Add(new(date, weekly is null, weekly is null ? null : WorkingDayExclusionReason.WeeklyOff, null));
        }

        return Result<WorkingDayCalculationResult>.Success(new(fromDate, toDate, details));
    }

    private static bool Applies(Guid? countryId, Guid? workLocationId, Guid? employeeCountryId, Guid? employeeWorkLocationId) =>
        (countryId is null || countryId == employeeCountryId) &&
        (workLocationId is null || workLocationId == employeeWorkLocationId);

    private static (HolidaySnapshot? Selected, bool Ambiguous) SelectMostSpecificHoliday(IReadOnlyList<HolidaySnapshot> matches)
    {
        if (matches.Count == 0) return (null, false);
        var max = matches.Max(Specificity);
        var selected = matches.Where(x => Specificity(x) == max).ToList();
        return selected.Count == 1 ? (selected[0], false) : (null, true);
    }

    private static (WeeklyOffSnapshot? Selected, bool Ambiguous) SelectMostSpecificWeeklyOff(IReadOnlyList<WeeklyOffSnapshot> matches)
    {
        if (matches.Count == 0) return (null, false);
        var max = matches.Max(Specificity);
        var selected = matches.Where(x => Specificity(x) == max).ToList();
        return selected.Count == 1 ? (selected[0], false) : (null, true);
    }

    private static int Specificity(HolidaySnapshot x) => x.WorkLocationId is not null ? 3 : x.CountryLocationId is not null ? 2 : 1;
    private static int Specificity(WeeklyOffSnapshot x) => x.WorkLocationId is not null ? 3 : x.CountryLocationId is not null ? 2 : 1;

    private sealed record HolidaySnapshot(DateOnly Date, string Name, Guid? CountryLocationId, Guid? WorkLocationId);
    private sealed record WeeklyOffSnapshot(Guid Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid? CountryLocationId, Guid? WorkLocationId, List<DayOfWeek> Days);
}
