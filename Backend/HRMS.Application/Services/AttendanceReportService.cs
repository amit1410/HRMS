using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceReportService(IHrmsDbContext db, ITenantContext tenant, IAttendanceMonthlyProcessor monthly) : IAttendanceReportService
{
    private const int MaxInteractiveDays = 366;
    private const int MaxExportRows = 50_000;

    public async Task<Result<PagedResult<AttendanceDailyReportRow>>> GetDailyAsync(AttendanceDailyReportQuery query, CancellationToken ct = default)
    {
        var validation = ValidateDates(query.FromDate, query.ToDate, MaxInteractiveDays);
        if (validation is not null) return Result<PagedResult<AttendanceDailyReportRow>>.Invalid("dateRange", validation);
        if (!ValidPage(query)) return Result<PagedResult<AttendanceDailyReportRow>>.Invalid("page", "Page values are out of range.");
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceDailyReportRow>>.Unauthorized("No authenticated tenant.");
        var from = query.FromDate!.Value; var to = query.ToDate!.Value;
        var source = DailySource(tenantId, from, to, query);
        var total = await source.CountAsync(ct);
        var page = await source.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var rows = await EnrichDailyAsync(page, ct);
        return Result<PagedResult<AttendanceDailyReportRow>>.Success(new(rows, query.Page, query.PageSize, total));
    }

    public async Task<Result<PagedResult<AttendanceMonthlyReportRow>>> GetMonthlyAsync(AttendanceMonthlyReportQuery query, CancellationToken ct = default)
    {
        if (!ValidPage(query)) return Result<PagedResult<AttendanceMonthlyReportRow>>.Invalid("page", "Page values are out of range.");
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceMonthlyReportRow>>.Unauthorized("No authenticated tenant.");
        if (query.Month is < 1 or > 12 || query.Year is < 1 or > 9999) return Result<PagedResult<AttendanceMonthlyReportRow>>.Invalid("period", "A valid year and month are required.");
        var source = from s in db.EmployeeAttendanceMonthlySummaries.AsNoTracking()
                     join p in db.AttendancePeriods.AsNoTracking() on new { s.TenantId, Id = s.AttendancePeriodId } equals new { p.TenantId, Id = p.Id }
                     where s.TenantId == tenantId && (!query.PeriodId.HasValue || s.AttendancePeriodId == query.PeriodId) && (!query.Year.HasValue || p.Year == query.Year) && (!query.Month.HasValue || p.Month == query.Month) && (!query.EmployeeId.HasValue || s.EmployeeId == query.EmployeeId)
                     orderby p.Year, p.Month, s.EmployeeCode, s.EmployeeId
                     select new AttendanceMonthlyReportRow(p.Id, p.Year, p.Month, p.Status, s.EmployeeCode, s.EmployeeName, s.WorkingDays, s.PresentDays, s.AbsentDays, s.OnLeaveDays, s.OnDutyDays, s.IncompleteDays, s.NotProcessedDays, s.ExpectedWorkMinutes, s.ActualWorkMinutes, s.ExceptionCount, s.SourceDataVersion);
        var total = await source.CountAsync(ct);
        var rows = await source.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<AttendanceMonthlyReportRow>>.Success(new(rows, query.Page, query.PageSize, total));
    }

    public async Task<Result<PagedResult<AttendanceExceptionDto>>> GetExceptionsAsync(AttendanceExceptionReportQuery query, CancellationToken ct = default)
    {
        var validation = ValidateDates(query.FromDate, query.ToDate, MaxInteractiveDays, allowMissing: true);
        if (validation is not null) return Result<PagedResult<AttendanceExceptionDto>>.Invalid("dateRange", validation);
        if (!ValidPage(query)) return Result<PagedResult<AttendanceExceptionDto>>.Invalid("page", "Page values are out of range.");
        var result = await monthly.GetExceptionsAsync(query.PeriodId, new AttendanceExceptionQuery { Page = 1, PageSize = PagedQuery.MaxPageSize }, ct);
        if (!result.Succeeded) return Result<PagedResult<AttendanceExceptionDto>>.Failure(result.Status, result.Message, result.Errors);
        var allExceptions = result.Value!.Items.ToList();
        for (var page = 2; allExceptions.Count < result.Value.TotalCount; page++)
        {
            var next = await monthly.GetExceptionsAsync(query.PeriodId, new AttendanceExceptionQuery { Page = page, PageSize = PagedQuery.MaxPageSize }, ct);
            if (!next.Succeeded) return Result<PagedResult<AttendanceExceptionDto>>.Failure(next.Status, next.Message, next.Errors);
            allExceptions.AddRange(next.Value!.Items);
        }
        IEnumerable<AttendanceExceptionDto> filtered = allExceptions;
        if (query.EmployeeId is Guid employeeId) filtered = filtered.Where(x => x.EmployeeId == employeeId);
        if (query.ExceptionType is AttendanceExceptionType type) filtered = filtered.Where(x => x.ExceptionType == type);
        if (query.IsBlocking is bool blocking) filtered = filtered.Where(x => x.IsBlocking == blocking);
        if (query.FromDate is DateOnly from) filtered = filtered.Where(x => x.BusinessDate >= from);
        if (query.ToDate is DateOnly to) filtered = filtered.Where(x => x.BusinessDate <= to);
        var list = filtered.OrderByDescending(x => x.BusinessDate).ThenBy(x => x.EmployeeId).ThenBy(x => x.ExceptionType).ToList();
        return Result<PagedResult<AttendanceExceptionDto>>.Success(new(list.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), query.Page, query.PageSize, list.Count));
    }

    public async Task<Result<AttendanceReportExport>> ExportDailyAsync(AttendanceDailyReportQuery query, CancellationToken ct = default)
    {
        var validation = ValidateDates(query.FromDate, query.ToDate, 3660); if (validation is not null) return Result<AttendanceReportExport>.Invalid("dateRange", validation);
        if (!TryTenant(out var tenantId)) return Result<AttendanceReportExport>.Unauthorized("No authenticated tenant.");
        var source = DailySource(tenantId, query.FromDate!.Value, query.ToDate!.Value, query); var total = await source.CountAsync(ct); if (total > MaxExportRows) return Result<AttendanceReportExport>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again.");
        var page = await source.Take(MaxExportRows).ToListAsync(ct);
        var rows = await EnrichDailyAsync(page, ct);
        var csv = new CsvBuilder("Employee Code", "Employee Name", "Business Date", "Status", "In Time UTC", "Out Time UTC", "Actual Work Minutes", "Expected Work Minutes", "Late", "Early Out", "Shift", "Department", "Work Location");
        foreach (var x in rows) csv.AppendRow(x.EmployeeCode, x.EmployeeName, x.BusinessDate.ToString("yyyy-MM-dd"), x.Status.ToString(), x.InTimeUtc?.ToString("O"), x.OutTimeUtc?.ToString("O"), x.ActualWorkMinutes?.ToString(), x.ExpectedWorkMinutes?.ToString(), x.IsLateIn.ToString(), x.IsEarlyOut.ToString(), x.ShiftCode, x.Department, x.WorkLocation);
        return Result<AttendanceReportExport>.Success(new($"attendance-daily-{query.FromDate:yyyy-MM-dd}-to-{query.ToDate:yyyy-MM-dd}.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount));
    }

    public async Task<Result<AttendanceReportExport>> ExportMonthlyAsync(AttendanceMonthlyReportQuery query, CancellationToken ct = default)
    {
        var all = await GetAllMonthlyAsync(query, ct); if (!all.Succeeded) return Result<AttendanceReportExport>.Failure(all.Status, all.Message, all.Errors); if (all.Value!.Count > MaxExportRows) return Result<AttendanceReportExport>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again.");
        var csv = new CsvBuilder("Year", "Month", "Period Status", "Employee Code", "Employee Name", "Working Days", "Present Days", "Absent Days", "Leave Days", "On Duty Days", "Incomplete Days", "Not Processed Days", "Expected Work Minutes", "Actual Work Minutes", "Exception Count", "Source Data Version"); foreach (var x in all.Value) csv.AppendRow(x.Year.ToString(), x.Month.ToString(), x.PeriodStatus.ToString(), x.EmployeeCode, x.EmployeeName, x.WorkingDays.ToString(), x.PresentDays.ToString(), x.AbsentDays.ToString(), x.OnLeaveDays.ToString(), x.OnDutyDays.ToString(), x.IncompleteDays.ToString(), x.NotProcessedDays.ToString(), x.ExpectedWorkMinutes.ToString(), x.ActualWorkMinutes.ToString(), x.ExceptionCount.ToString(), x.SourceDataVersion.ToString());
        return Result<AttendanceReportExport>.Success(new("attendance-monthly.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount));
    }

    public async Task<Result<AttendanceReportExport>> ExportExceptionsAsync(AttendanceExceptionReportQuery query, CancellationToken ct = default)
    {
        var all = await GetAllExceptionsAsync(query, ct); if (!all.Succeeded) return Result<AttendanceReportExport>.Failure(all.Status, all.Message, all.Errors); if (all.Value!.Count > MaxExportRows) return Result<AttendanceReportExport>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again.");
        var csv = new CsvBuilder("Period Id", "Employee Id", "Business Date", "Exception Type", "Blocking", "Source Id", "Message"); foreach (var x in all.Value) csv.AppendRow(x.PeriodId.ToString("D"), x.EmployeeId.ToString("D"), x.BusinessDate.ToString("yyyy-MM-dd"), x.ExceptionType.ToString(), x.IsBlocking.ToString(), x.SourceId?.ToString("D"), x.Message);
        return Result<AttendanceReportExport>.Success(new("attendance-exceptions.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount));
    }

    private async Task<Result<List<AttendanceMonthlyReportRow>>> GetAllMonthlyAsync(AttendanceMonthlyReportQuery query, CancellationToken ct)
    {
        var q = new AttendanceMonthlyReportQuery { PeriodId = query.PeriodId, Year = query.Year, Month = query.Month, EmployeeId = query.EmployeeId, Page = 1, PageSize = PagedQuery.MaxPageSize };
        var page = await GetMonthlyAsync(q, ct);
        if (!page.Succeeded) return Result<List<AttendanceMonthlyReportRow>>.Failure(page.Status, page.Message, page.Errors);
        if (page.Value!.TotalCount > MaxExportRows) return Result<List<AttendanceMonthlyReportRow>>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again.");
        var rows = page.Value.Items.ToList();
        while (rows.Count < page.Value.TotalCount) { q.Page++; var next = await GetMonthlyAsync(q, ct); if (!next.Succeeded) return Result<List<AttendanceMonthlyReportRow>>.Failure(next.Status, next.Message, next.Errors); rows.AddRange(next.Value!.Items); }
        return Result<List<AttendanceMonthlyReportRow>>.Success(rows);
    }

    private async Task<Result<List<AttendanceExceptionDto>>> GetAllExceptionsAsync(AttendanceExceptionReportQuery query, CancellationToken ct)
    {
        var q = new AttendanceExceptionReportQuery { PeriodId = query.PeriodId, EmployeeId = query.EmployeeId, ExceptionType = query.ExceptionType, IsBlocking = query.IsBlocking, FromDate = query.FromDate, ToDate = query.ToDate, Page = 1, PageSize = PagedQuery.MaxPageSize };
        var page = await GetExceptionsAsync(q, ct);
        if (!page.Succeeded) return Result<List<AttendanceExceptionDto>>.Failure(page.Status, page.Message, page.Errors);
        if (page.Value!.TotalCount > MaxExportRows) return Result<List<AttendanceExceptionDto>>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again.");
        var rows = page.Value.Items.ToList();
        while (rows.Count < page.Value.TotalCount) { q.Page++; var next = await GetExceptionsAsync(q, ct); if (!next.Succeeded) return Result<List<AttendanceExceptionDto>>.Failure(next.Status, next.Message, next.Errors); rows.AddRange(next.Value!.Items); }
        return Result<List<AttendanceExceptionDto>>.Success(rows);
    }

    private IQueryable<DailySourceRow> DailySource(Guid tenantId, DateOnly fromDate, DateOnly toDate, AttendanceDailyReportQuery query)
    {
        var days = db.EmployeeAttendanceDays.AsNoTracking().Where(d => d.TenantId == tenantId && d.BusinessDate >= fromDate && d.BusinessDate <= toDate);
        if (query.EmployeeId is Guid employeeId) days = days.Where(d => d.EmployeeId == employeeId);
        if (query.Status is EmployeeAttendanceDayStatus status) days = days.Where(d => d.Status == status);
        if (query.DepartmentId is Guid departmentId) days = days.Where(d => db.EmployeeEmploymentHistory.Any(h => h.TenantId == tenantId && h.EmployeeId == d.EmployeeId && h.EffectiveFrom <= d.BusinessDate && (h.EffectiveTo == null || h.EffectiveTo >= d.BusinessDate) && h.DepartmentId == departmentId));
        if (query.WorkLocationId is Guid locationId) days = days.Where(d => db.EmployeeEmploymentHistory.Any(h => h.TenantId == tenantId && h.EmployeeId == d.EmployeeId && h.EffectiveFrom <= d.BusinessDate && (h.EffectiveTo == null || h.EffectiveTo >= d.BusinessDate) && h.WorkLocationId == locationId));
        return from d in days join e in db.Employees.AsNoTracking() on new { d.TenantId, d.EmployeeId } equals new { e.TenantId, EmployeeId = e.Id } orderby d.BusinessDate descending, e.EmployeeCode, d.EmployeeId select new DailySourceRow(d.EmployeeId, e.EmployeeCode, (e.FirstName + " " + e.LastName).Trim(), d.BusinessDate, d.Status, d.FirstPunchAtUtc, d.LastPunchAtUtc, d.WorkedMinutes, d.ExpectedWorkMinutes, d.IsLateIn, d.IsEarlyOut, d.ShiftCode);
    }

    private async Task<List<AttendanceDailyReportRow>> EnrichDailyAsync(IReadOnlyList<DailySourceRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var employeeIds = rows.Select(x => x.EmployeeId).Distinct().ToList();
        var histories = new List<EmploymentProjection>();
        foreach (var batch in employeeIds.Chunk(500))
        {
            var batchHistories = await db.EmployeeEmploymentHistory.AsNoTracking().Where(x => batch.Contains(x.EmployeeId)).Select(x => new EmploymentProjection(x.EmployeeId, x.EffectiveFrom, x.EffectiveTo, x.DepartmentName, x.WorkLocation == null ? null : x.WorkLocation.Name)).ToListAsync(ct);
            histories.AddRange(batchHistories);
        }
        return rows.Select(x => { var h = histories.Where(h => h.EmployeeId == x.EmployeeId && h.EffectiveFrom <= x.BusinessDate && (h.EffectiveTo == null || h.EffectiveTo >= x.BusinessDate)).OrderByDescending(h => h.EffectiveFrom).FirstOrDefault(); return new AttendanceDailyReportRow(x.EmployeeId, x.EmployeeCode, x.EmployeeName, x.BusinessDate, x.Status, x.InTime, x.OutTime, x.WorkedMinutes, x.ExpectedWorkMinutes, x.IsLateIn, x.IsEarlyOut, x.ShiftCode, h?.DepartmentName, h?.WorkLocation); }).ToList();
    }

    private sealed record DailySourceRow(Guid EmployeeId, string? EmployeeCode, string EmployeeName, DateOnly BusinessDate, EmployeeAttendanceDayStatus Status, DateTime? InTime, DateTime? OutTime, int? WorkedMinutes, int? ExpectedWorkMinutes, bool IsLateIn, bool IsEarlyOut, string? ShiftCode);
    private sealed record EmploymentProjection(Guid EmployeeId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? DepartmentName, string? WorkLocation);
    private static bool ValidPage(PagedQuery q) => q.Page >= 1 && q.PageSize is >= 1 and <= PagedQuery.MaxPageSize;
    private bool TryTenant(out Guid id) { id = tenant.TenantId ?? Guid.Empty; return id != Guid.Empty; }
    private static string? ValidateDates(DateOnly? from, DateOnly? to, int maxDays, bool allowMissing = false) { if (allowMissing && from is null && to is null) return null; if (from is null || to is null) return "Both FromDate and ToDate are required."; if (from > to) return "FromDate cannot be after ToDate."; return to.Value.DayNumber - from.Value.DayNumber + 1 > maxDays ? $"The date range cannot exceed {maxDays} days." : null; }
}
