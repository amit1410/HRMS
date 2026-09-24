using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HRMS.Application.Services;

public sealed class AttendanceMonthlyProcessor(IHrmsDbContext db, ITenantContext tenant, TimeProvider? timeProvider = null, IAttendanceAuthorizationService? authorization = null) : IAttendanceMonthlyProcessor
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<Result<AttendancePeriodDto>> CreatePeriodAsync(AttendancePeriodRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out var userId)) return Result<AttendancePeriodDto>.Unauthorized("No authenticated tenant.");
        if (request.Month is < 1 or > 12 || request.Year is < 1 or > 9999) return Result<AttendancePeriodDto>.Invalid("period", "A valid calendar year and month are required.");
        var start = new DateOnly(request.Year, request.Month, 1); var end = start.AddMonths(1).AddDays(-1);
        if (await db.AttendancePeriods.AnyAsync(x => x.TenantId == tenantId && x.Year == request.Year && x.Month == request.Month, ct)) return Result<AttendancePeriodDto>.Conflict("The Attendance period already exists.");
        var period = new AttendancePeriod { Id = Guid.NewGuid(), TenantId = tenantId, Year = request.Year, Month = request.Month, StartDate = start, EndDate = end, CreatedByUserId = userId };
        db.AttendancePeriods.Add(period);
        db.AttendancePeriodEvents.Add(Event(period, AttendancePeriodEventType.Created, userId, "Period created."));
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Result<AttendancePeriodDto>.Conflict("The Attendance period already exists."); }
        return Result<AttendancePeriodDto>.Success(Map(period));
    }

    public async Task<Result<PagedResult<AttendancePeriodDto>>> GetPeriodsAsync(AttendancePeriodQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out _)) return Result<PagedResult<AttendancePeriodDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query)) return Result<PagedResult<AttendancePeriodDto>>.Invalid("page", "Page values are out of range.");
        var q = db.AttendancePeriods.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.Year is int year) q = q.Where(x => x.Year == year);
        if (query.Month is int month) q = q.Where(x => x.Month == month);
        if (query.Status is AttendancePeriodStatus status) q = q.Where(x => x.Status == status);
        var total = await q.CountAsync(ct); var items = await q.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<AttendancePeriodDto>>.Success(new(items.Select(Map).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<AttendancePeriodDto>> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out _)) return Result<AttendancePeriodDto>.Unauthorized("No authenticated tenant.");
        var period = await db.AttendancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        return period is null ? Result<AttendancePeriodDto>.NotFound("Attendance period was not found.") : Result<AttendancePeriodDto>.Success(Map(period));
    }

    public async Task<Result<AttendancePeriodOverviewDto>> ProcessAsync(Guid periodId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out var userId)) return Result<AttendancePeriodOverviewDto>.Unauthorized("No authenticated tenant.");
        var period = await db.AttendancePeriods.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        if (period is null) return Result<AttendancePeriodOverviewDto>.NotFound("Attendance period was not found.");
        if (period.Status == AttendancePeriodStatus.Closed) return Result<AttendancePeriodOverviewDto>.Conflict("A closed Attendance period must be reopened before processing.");
        if (period.Status == AttendancePeriodStatus.Processing) return Result<AttendancePeriodOverviewDto>.Conflict("The Attendance period is already processing.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        var runId = Guid.NewGuid(); var now = clock.GetUtcNow().UtcDateTime; period.Status = AttendancePeriodStatus.Processing; period.ConcurrencyVersion++; period.ProcessingRunId = runId;
        db.AttendancePeriodEvents.Add(Event(period, AttendancePeriodEventType.ProcessingStarted, userId, "Monthly processing started.", runId));
        try
        {
            await db.SaveChangesAsync(ct);
            var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.DateOfJoining <= period.EndDate && (x.DateOfLeaving == null || x.DateOfLeaving >= period.StartDate)).OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id).ToListAsync(ct);
            var existingSummaries = await db.EmployeeAttendanceMonthlySummaries
                .Where(x => x.TenantId == tenantId && x.AttendancePeriodId == period.Id)
                .ToListAsync(ct);
            db.EmployeeAttendanceMonthlySummaries.RemoveRange(existingSummaries);
            var summaries = new List<EmployeeAttendanceMonthlySummary>();
            foreach (var employee in employees)
            {
                var histories = await db.EmployeeEmploymentHistory.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && !x.IsSuperseded && x.EffectiveFrom <= period.EndDate && (x.EffectiveTo == null || x.EffectiveTo >= period.StartDate)).ToListAsync(ct);
                var days = await db.EmployeeAttendanceDays.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && x.BusinessDate >= period.StartDate && x.BusinessDate <= period.EndDate)
                    .ToDictionaryAsync(x => x.BusinessDate, ct);
                var summary = new EmployeeAttendanceMonthlySummary { Id = Guid.NewGuid(), TenantId = tenantId, AttendancePeriodId = period.Id, EmployeeId = employee.Id, EmployeeCode = employee.EmployeeCode, EmployeeName = (employee.FirstName + " " + employee.LastName).Trim(), CalendarDays = period.EndDate.DayNumber - period.StartDate.DayNumber + 1, SourceDataVersion = period.DataVersion, ProcessedAtUtc = now };
                var exceptions = new List<AttendanceExceptionDto>();
                for (var date = period.StartDate; date <= period.EndDate; date = date.AddDays(1))
                {
                    if (!histories.Any(h => h.EffectiveFrom <= date && (h.EffectiveTo == null || h.EffectiveTo >= date))) continue;
                    summary.EmploymentDays++;
                    days.TryGetValue(date, out var day);
                    AddDay(summary, day, tenantId, period.Id, employee.Id, date, exceptions);
                    if (day?.Status == EmployeeAttendanceDayStatus.Present) summary.PresentDayQuantity += 1m;
                    if (day?.Status == EmployeeAttendanceDayStatus.OnLeave) summary.PaidLeaveDays += 1m;
                    if (day?.Status == EmployeeAttendanceDayStatus.Absent) summary.LopDays += 1m;
                    if (await db.AttendanceAdjustments.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && x.BusinessDate == date, ct)) summary.RegularizedDays++;
                }
                var pendingReg = await db.AttendanceRegularizationRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && x.BusinessDate >= period.StartDate && x.BusinessDate <= period.EndDate && x.Status == AttendanceRequestStatus.Pending).ToListAsync(ct);
                exceptions.AddRange(pendingReg.Select(x => Exception(period.Id, employee.Id, x.BusinessDate, AttendanceExceptionType.PendingRegularization, true, x.Id, "Pending Regularization blocks monthly processing.")));
                var pendingOd = await db.AttendanceOnDutyRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && x.StartDate <= period.EndDate && x.EndDate >= period.StartDate && x.Status == AttendanceRequestStatus.Pending).ToListAsync(ct);
                exceptions.AddRange(pendingOd.Select(x => Exception(period.Id, employee.Id, x.StartDate < period.StartDate ? period.StartDate : x.StartDate, AttendanceExceptionType.PendingOnDuty, true, x.Id, "Pending On Duty blocks monthly processing.")));
                summary.ExceptionCount = exceptions.Count;
                summary.PayableDays = Math.Max(0m, summary.EmploymentDays - summary.LopDays);
                summary.PresentDayQuantity = summary.PresentDays;
                summaries.Add(summary);
            }
            db.EmployeeAttendanceMonthlySummaries.AddRange(summaries);
            period.Status = AttendancePeriodStatus.ReadyToClose; period.ProcessedAtUtc = now; period.LastProcessedByUserId = userId; period.ConcurrencyVersion++;
            db.AttendancePeriodEvents.Add(Event(period, AttendancePeriodEventType.ProcessingCompleted, userId, "Monthly processing completed.", runId));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            var derived = await DeriveExceptions(period, ct, Permissions.Attendance.MonthlyViewAll);
            return Result<AttendancePeriodOverviewDto>.Success(Overview(period, summaries, derived.Count(x => x.IsBlocking)));
        }
        catch (DbUpdateConcurrencyException) { return Result<AttendancePeriodOverviewDto>.Conflict("The Attendance period was changed by another processing operation."); }
        catch
        {
            await transaction.RollbackAsync(ct);
            return Result<AttendancePeriodOverviewDto>.Failure(ResultStatus.ServiceUnavailable, "Monthly Attendance processing failed.");
        }
    }

    public async Task<Result<AttendancePeriodClosePreviewDto>> GetClosePreviewAsync(Guid periodId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out _)) return Result<AttendancePeriodClosePreviewDto>.Unauthorized("No authenticated tenant.");
        var period = await db.AttendancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        if (period is null) return Result<AttendancePeriodClosePreviewDto>.NotFound("Attendance period was not found.");
        return Result<AttendancePeriodClosePreviewDto>.Success(await BuildClosePreviewAsync(period, ct));
    }

    public async Task<Result<AttendancePeriodDto>> CloseAsync(Guid periodId, AttendancePeriodCommandRequest? request = null, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out var userId)) return Result<AttendancePeriodDto>.Unauthorized("No authenticated tenant.");
        var period = await db.AttendancePeriods.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        if (period is null) return Result<AttendancePeriodDto>.NotFound("Attendance period was not found.");
        if (period.Status == AttendancePeriodStatus.Closed) return Result<AttendancePeriodDto>.Conflict("The Attendance period is already closed.");
        if (await db.PayrollRuns.AnyAsync(x => x.TenantId == tenantId && x.PayrollPeriod!.StartDate == period.StartDate && x.PayrollPeriod.EndDate == period.EndDate && x.Status == PayrollRunStatus.Finalized, ct)) return Result<AttendancePeriodDto>.Conflict("PayrollAlreadyFinalized: Attendance correction requires a controlled payroll correction path.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        var preview = await BuildClosePreviewAsync(period, ct);
        if (!preview.CanClose) return Result<AttendancePeriodDto>.Conflict(string.Join(" ", preview.Blockers));
        var summaries = await db.EmployeeAttendanceMonthlySummaries.Where(x => x.TenantId == tenantId && x.AttendancePeriodId == period.Id).ToListAsync(ct);
        foreach (var summary in summaries)
        {
            var previous = await db.PayrollAttendanceSnapshots.Where(x => x.TenantId == tenantId && x.AttendancePeriodId == period.Id && x.EmployeeId == summary.EmployeeId && x.IsCurrent).ToListAsync(ct);
            foreach (var old in previous) old.IsCurrent = false;
            var version = previous.Select(x => x.Version).DefaultIfEmpty(0).Max();
            var source = $"{period.Id:N}:{summary.EmployeeId:N}:{summary.SourceDataVersion}:{summary.PayableDays}:{summary.LopDays}";
            db.PayrollAttendanceSnapshots.Add(new PayrollAttendanceSnapshot { Id = Guid.NewGuid(), TenantId = tenantId, AttendancePeriodId = period.Id, EmployeeId = summary.EmployeeId, Version = version + 1, IsCurrent = true, PeriodStart = period.StartDate, PeriodEnd = period.EndDate, EligibleDays = summary.EmploymentDays, PayableDays = summary.PayableDays, LopDays = summary.LopDays, PresentDays = summary.PresentDayQuantity, AbsentDays = summary.AbsentDays, PaidLeaveDays = summary.PaidLeaveDays, UnpaidLeaveDays = summary.UnpaidLeaveDays, HolidayDays = summary.HolidayDays, WeekOffDays = summary.WeeklyOffDays, OnDutyDays = summary.OnDutyDays, FinalizedByUserId = userId, FinalizedAtUtc = clock.GetUtcNow().UtcDateTime, SourceHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))) });
            db.AttendancePeriodEvents.Add(Event(period, previous.Count == 0 ? AttendancePeriodEventType.PayrollSnapshotCreated : AttendancePeriodEventType.PayrollSnapshotSuperseded, userId, $"Payroll Attendance snapshot version {version + 1} created for employee {summary.EmployeeId}."));
        }
        period.Status = AttendancePeriodStatus.Closed;
        period.ConcurrencyVersion++;
        db.AttendancePeriodEvents.Add(Event(period, AttendancePeriodEventType.Closed, userId, string.IsNullOrWhiteSpace(request?.Comment) ? "Attendance period closed." : request.Comment.Trim()));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<AttendancePeriodDto>.Success(Map(period));
        }
        catch (DbUpdateConcurrencyException) { return Result<AttendancePeriodDto>.Conflict("The Attendance period was changed by another operation."); }
        catch { await transaction.RollbackAsync(ct); return Result<AttendancePeriodDto>.Failure(ResultStatus.ServiceUnavailable, "The Attendance period could not be closed."); }
    }

    public async Task<Result<AttendancePeriodDto>> ReopenAsync(Guid periodId, AttendancePeriodReopenRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out var userId)) return Result<AttendancePeriodDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result<AttendancePeriodDto>.Invalid("reason", "A reopen reason is required.");
        if (request.Reason.Trim().Length > 2000) return Result<AttendancePeriodDto>.Invalid("reason", "The reopen reason is too long.");
        var period = await db.AttendancePeriods.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        if (period is null) return Result<AttendancePeriodDto>.NotFound("Attendance period was not found.");
        if (period.Status != AttendancePeriodStatus.Closed) return Result<AttendancePeriodDto>.Conflict("Only a closed Attendance period can be reopened.");
        if (await db.PayrollRuns.AnyAsync(x => x.TenantId == tenantId && x.PayrollPeriod!.StartDate == period.StartDate && x.PayrollPeriod.EndDate == period.EndDate && x.Status == PayrollRunStatus.Finalized, ct)) return Result<AttendancePeriodDto>.Conflict("PayrollAlreadyFinalized: Attendance correction requires a controlled payroll correction path.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        period.Status = AttendancePeriodStatus.Open;
        period.DataVersion++;
        period.ConcurrencyVersion++;
        db.AttendancePeriodEvents.Add(Event(period, AttendancePeriodEventType.Reopened, userId, request.Reason.Trim()));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<AttendancePeriodDto>.Success(Map(period));
        }
        catch (DbUpdateConcurrencyException) { return Result<AttendancePeriodDto>.Conflict("The Attendance period was changed by another operation."); }
        catch { await transaction.RollbackAsync(ct); return Result<AttendancePeriodDto>.Failure(ResultStatus.ServiceUnavailable, "The Attendance period could not be reopened."); }
    }

    public async Task<Result<IReadOnlyList<AttendancePeriodEventDto>>> GetEventsAsync(Guid periodId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out _)) return Result<IReadOnlyList<AttendancePeriodEventDto>>.Unauthorized("No authenticated tenant.");
        if (!await db.AttendancePeriods.AnyAsync(x => x.TenantId == tenantId && x.Id == periodId, ct)) return Result<IReadOnlyList<AttendancePeriodEventDto>>.NotFound("Attendance period was not found.");
        var events = await db.AttendancePeriodEvents.AsNoTracking().Where(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).Select(x => new AttendancePeriodEventDto(x.Id, x.AttendancePeriodId, x.EventType, x.ActorUserId, x.OccurredAtUtc, x.DataVersion, x.Details)).ToListAsync(ct);
        return Result<IReadOnlyList<AttendancePeriodEventDto>>.Success(events);
    }

    public async Task<Result<AttendancePeriodOverviewDto>> GetOverviewAsync(Guid periodId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out _)) return Result<AttendancePeriodOverviewDto>.Unauthorized("No authenticated tenant.");
        var period = await db.AttendancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        if (period is null) return Result<AttendancePeriodOverviewDto>.NotFound("Attendance period was not found.");
        var summariesQuery = db.EmployeeAttendanceMonthlySummaries.AsNoTracking().Where(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId);
        if (authorization is not null)
        {
            var scope = await authorization.BuildEmployeePredicateAsync(Permissions.Attendance.MonthlyViewAll, true, true, true, period.StartDate, ct);
            if (!scope.Succeeded || scope.Value is null) return Result<AttendancePeriodOverviewDto>.Failure(scope.Status, scope.Message, scope.Errors);
            summariesQuery = summariesQuery.Where(x => db.Employees.Where(scope.Value).Any(e => e.Id == x.EmployeeId));
        }
        var summaries = await summariesQuery.ToListAsync(ct);
        var exceptions = await DeriveExceptions(period, ct, Permissions.Attendance.MonthlyViewAll);
        return Result<AttendancePeriodOverviewDto>.Success(Overview(period, summaries, exceptions.Count(x => x.IsBlocking)));
    }

    public async Task<Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>> GetSummariesAsync(Guid periodId, AttendanceMonthlySummaryQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out _)) return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query)) return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.Invalid("page", "Page values are out of range.");
        var period = await db.AttendancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        if (period is null) return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.NotFound("Attendance period was not found.");
        var q = db.EmployeeAttendanceMonthlySummaries.AsNoTracking().Where(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId);
        if (authorization is not null)
        {
            var scope = await authorization.BuildEmployeePredicateAsync(Permissions.Attendance.MonthlyViewAll, true, true, true, period.StartDate, ct);
            if (!scope.Succeeded || scope.Value is null) return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.Failure(scope.Status, scope.Message, scope.Errors);
            q = q.Where(x => db.Employees.Where(scope.Value).Any(e => e.Id == x.EmployeeId));
        }
        if (query.EmployeeId is Guid employeeId) q = q.Where(x => x.EmployeeId == employeeId);
        if (query.HasExceptions is bool has) q = has ? q.Where(x => x.ExceptionCount > 0) : q.Where(x => x.ExceptionCount == 0);
        if (query.HasLop is bool lop) q = lop ? q.Where(x => x.LopDays > 0) : q.Where(x => x.LopDays == 0);
        var total = await q.CountAsync(ct); var rows = await q.OrderBy(x => x.EmployeeCode).ThenBy(x => x.EmployeeId).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.Success(new(rows.Select(Map).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>> GetMySummariesAsync(AttendanceMonthlySummaryQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out var userId)) return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query) || userId is null) return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.Invalid("page", "Page values are out of range.");
        var employeeId = await db.AccountEmployeeCurrentLinks.AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId.Value).Select(x => (Guid?)x.EmployeeId).SingleOrDefaultAsync(ct);
        if (employeeId is null) return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.NotFound("No employee identity is linked to this account.");
        var q = db.EmployeeAttendanceMonthlySummaries.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId.Value);
        if (query.HasLop is bool lop) q = lop ? q.Where(x => x.LopDays > 0) : q.Where(x => x.LopDays == 0);
        var total = await q.CountAsync(ct); var rows = await q.OrderByDescending(x => x.AttendancePeriodId).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>.Success(new(rows.Select(Map).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PagedResult<AttendanceExceptionDto>>> GetExceptionsAsync(Guid periodId, AttendanceExceptionQuery query, CancellationToken ct = default, string? authorizationPermission = null)
    {
        if (!TryTenant(out var tenantId, out _)) return Result<PagedResult<AttendanceExceptionDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query)) return Result<PagedResult<AttendanceExceptionDto>>.Invalid("page", "Page values are out of range.");
        var period = await db.AttendancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == periodId, ct);
        if (period is null) return Result<PagedResult<AttendanceExceptionDto>>.NotFound("Attendance period was not found.");
        var all = await DeriveExceptions(period, ct, authorizationPermission ?? Permissions.Attendance.MonthlyViewAll); IEnumerable<AttendanceExceptionDto> filtered = all;
        if (query.EmployeeId is Guid employeeId) filtered = filtered.Where(x => x.EmployeeId == employeeId);
        if (query.ExceptionType is AttendanceExceptionType type) filtered = filtered.Where(x => x.ExceptionType == type);
        if (query.IsBlocking is bool blocking) filtered = filtered.Where(x => x.IsBlocking == blocking);
        if (query.Date is DateOnly date) filtered = filtered.Where(x => x.BusinessDate == date);
        var list = filtered.OrderBy(x => x.BusinessDate).ThenBy(x => x.EmployeeId).ThenBy(x => x.ExceptionType).ToList();
        return Result<PagedResult<AttendanceExceptionDto>>.Success(new(list.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), query.Page, query.PageSize, list.Count));
    }

    private async Task<List<AttendanceExceptionDto>> DeriveExceptions(AttendancePeriod period, CancellationToken ct, string authorizationPermission)
    {
        var tenantId = period.TenantId; var employeesQuery = db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.DateOfJoining <= period.EndDate && (x.DateOfLeaving == null || x.DateOfLeaving >= period.StartDate));
        if (authorization is not null)
        {
            var scope = await authorization.BuildEmployeePredicateAsync(authorizationPermission, true, true, true, period.StartDate, ct);
            if (!scope.Succeeded || scope.Value is null) return [];
            employeesQuery = employeesQuery.Where(scope.Value);
        }
        var employeeIds = await employeesQuery.Select(x => x.Id).ToListAsync(ct);
        if (employeeIds.Count == 0) return [];

        var histories = await db.EmployeeEmploymentHistory.AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && !x.IsSuperseded && x.EffectiveFrom <= period.EndDate && (x.EffectiveTo == null || x.EffectiveTo >= period.StartDate))
            .ToListAsync(ct);
        var days = await db.EmployeeAttendanceDays.AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.BusinessDate >= period.StartDate && x.BusinessDate <= period.EndDate)
            .ToListAsync(ct);
        var regs = await db.AttendanceRegularizationRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.BusinessDate >= period.StartDate && x.BusinessDate <= period.EndDate && x.Status == AttendanceRequestStatus.Pending)
            .ToListAsync(ct);
        var ods = await db.AttendanceOnDutyRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.StartDate <= period.EndDate && x.EndDate >= period.StartDate && x.Status == AttendanceRequestStatus.Pending)
            .ToListAsync(ct);

        var historiesByEmployee = histories.GroupBy(x => x.EmployeeId).ToDictionary(x => x.Key, x => x.ToList());
        var daysByEmployee = days.GroupBy(x => x.EmployeeId).ToDictionary(x => x.Key, x => x.ToDictionary(x => x.BusinessDate));
        var result = new List<AttendanceExceptionDto>();
        foreach (var employeeId in employeeIds)
        {
            historiesByEmployee.TryGetValue(employeeId, out var employeeHistories);
            daysByEmployee.TryGetValue(employeeId, out var employeeDays);
            for (var date = period.StartDate; date <= period.EndDate; date = date.AddDays(1))
                if (employeeHistories?.Any(h => h.EffectiveFrom <= date && (h.EffectiveTo == null || h.EffectiveTo >= date)) == true)
                {
                    EmployeeAttendanceDay? day = null;
                    employeeDays?.TryGetValue(date, out day);
                    AddExceptions(result, day, period.Id, employeeId, date);
                }

            result.AddRange(regs.Where(x => x.EmployeeId == employeeId).Select(x => Exception(period.Id, employeeId, x.BusinessDate, AttendanceExceptionType.PendingRegularization, true, x.Id, "Pending Regularization blocks monthly processing.")));
            result.AddRange(ods.Where(x => x.EmployeeId == employeeId).Select(x => Exception(period.Id, employeeId, x.StartDate < period.StartDate ? period.StartDate : x.StartDate, AttendanceExceptionType.PendingOnDuty, true, x.Id, "Pending On Duty blocks monthly processing.")));
        }
        return result;
    }

    private async Task<AttendancePeriodClosePreviewDto> BuildClosePreviewAsync(AttendancePeriod period, CancellationToken ct)
    {
        var summaries = await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().Where(x => x.TenantId == period.TenantId && x.AttendancePeriodId == period.Id).ToListAsync(ct);
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == period.TenantId && x.DateOfJoining <= period.EndDate && (x.DateOfLeaving == null || x.DateOfLeaving >= period.StartDate)).Select(x => x.Id).ToListAsync(ct);
        var exceptions = await DeriveExceptions(period, ct, Permissions.Attendance.MonthlyViewAll);
        var pendingReg = exceptions.Count(x => x.ExceptionType == AttendanceExceptionType.PendingRegularization);
        var pendingOd = exceptions.Count(x => x.ExceptionType == AttendanceExceptionType.PendingOnDuty);
        var notProcessed = exceptions.Count(x => x.ExceptionType == AttendanceExceptionType.NotProcessed);
        var incomplete = exceptions.Count(x => x.ExceptionType == AttendanceExceptionType.Incomplete);
        var blockers = new List<string>();
        if (period.Status != AttendancePeriodStatus.ReadyToClose) blockers.Add("The Attendance period must be processed and ReadyToClose.");
        var current = summaries.Count == employees.Count && summaries.All(x => x.SourceDataVersion == period.DataVersion);
        if (!current) blockers.Add("Monthly summaries are missing or stale.");
        if (exceptions.Any(x => x.IsBlocking)) blockers.Add("Blocking Attendance exceptions remain.");
        return new(period.Id, period.Status, blockers.Count == 0, period.DataVersion, current, summaries.Count, exceptions.Count(x => x.IsBlocking), pendingReg, pendingOd, notProcessed, incomplete, blockers);
    }

    private static void AddDay(EmployeeAttendanceMonthlySummary s, EmployeeAttendanceDay? day, Guid tenantId, Guid periodId, Guid employeeId, DateOnly date, List<AttendanceExceptionDto> exceptions)
    {
        if (day is null) { s.NotProcessedDays++; exceptions.Add(Exception(periodId, employeeId, date, AttendanceExceptionType.NotProcessed, true, null, "Attendance day has not been processed.")); return; }
        switch (day.Status) { case EmployeeAttendanceDayStatus.Present: s.PresentDays++; s.WorkingDays++; break; case EmployeeAttendanceDayStatus.Absent: s.AbsentDays++; s.WorkingDays++; break; case EmployeeAttendanceDayStatus.OnLeave: s.OnLeaveDays++; break; case EmployeeAttendanceDayStatus.OnDuty: s.OnDutyDays++; s.ApprovedOnDutyDays++; break; case EmployeeAttendanceDayStatus.Holiday: s.HolidayDays++; break; case EmployeeAttendanceDayStatus.WeeklyOff: s.WeeklyOffDays++; break; case EmployeeAttendanceDayStatus.Incomplete: s.IncompleteDays++; s.WorkingDays++; exceptions.Add(Exception(periodId, employeeId, date, AttendanceExceptionType.Incomplete, true, day.Id, "Attendance day is incomplete.")); break; case EmployeeAttendanceDayStatus.NotProcessed: s.NotProcessedDays++; exceptions.Add(Exception(periodId, employeeId, date, AttendanceExceptionType.NotProcessed, true, day.Id, "Attendance day has not been processed.")); break; }
        if (day.IsLateIn) s.LateInCount++; if (day.IsEarlyOut) s.EarlyOutCount++; if (day.IsGraceApplied) s.GraceAppliedCount++; if (day.HasMissingInPunch) { s.MissingInCount++; exceptions.Add(Exception(periodId, employeeId, date, AttendanceExceptionType.MissingInPunch, true, day.Id, "In punch is missing.")); } if (day.HasMissingOutPunch) { s.MissingOutCount++; exceptions.Add(Exception(periodId, employeeId, date, AttendanceExceptionType.MissingOutPunch, true, day.Id, "Out punch is missing.")); } if (day.LeaveConflict) { s.LeaveConflictCount++; exceptions.Add(Exception(periodId, employeeId, date, AttendanceExceptionType.LeaveConflict, false, day.Id, "Attendance conflicts with approved Leave.")); }
        s.ExpectedWorkMinutes += day.ExpectedWorkMinutes ?? 0; s.ActualWorkMinutes += day.WorkedMinutes ?? 0;
    }

    private static void AddExceptions(List<AttendanceExceptionDto> target, EmployeeAttendanceDay? day, Guid periodId, Guid employeeId, DateOnly date) { var s = new EmployeeAttendanceMonthlySummary(); AddDay(s, day, Guid.Empty, periodId, employeeId, date, target); }
    private static AttendanceExceptionDto Exception(Guid periodId, Guid employeeId, DateOnly date, AttendanceExceptionType type, bool blocking, Guid? source, string message) => new(StableId($"{periodId:N}:{employeeId:N}:{date:yyyyMMdd}:{type}:{source:N}"), periodId, employeeId, date, type, blocking, source, message);
    private static Guid StableId(string value) => new(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))[..16]);
    private AttendancePeriodEvent Event(AttendancePeriod p, AttendancePeriodEventType type, Guid? actor, string details, Guid? run = null) => new() { Id = Guid.NewGuid(), TenantId = p.TenantId, AttendancePeriodId = p.Id, EventType = type, ActorUserId = actor, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, DataVersion = p.DataVersion, Details = details, ProcessingRunId = run };
    private bool TryTenant(out Guid tenantId, out Guid? userId) { tenantId = tenant.TenantId ?? Guid.Empty; userId = tenant.UserId; return tenantId != Guid.Empty; }
    private static bool ValidPage(PagedQuery q) => q.Page >= 1 && q.PageSize is >= 1 and <= PagedQuery.MaxPageSize;
    private static AttendancePeriodDto Map(AttendancePeriod x) => new(x.Id, x.Year, x.Month, x.StartDate, x.EndDate, x.Status, x.DataVersion, x.ConcurrencyVersion, x.ProcessedAtUtc, x.LastProcessedByUserId);
    private static EmployeeAttendanceMonthlySummaryDto Map(EmployeeAttendanceMonthlySummary x) => new(x.Id, x.AttendancePeriodId, x.EmployeeId, x.EmployeeCode, x.EmployeeName, x.CalendarDays, x.EmploymentDays, x.WorkingDays, x.PresentDays, x.AbsentDays, x.OnLeaveDays, x.OnDutyDays, x.HolidayDays, x.WeeklyOffDays, x.IncompleteDays, x.NotProcessedDays, x.LateInCount, x.EarlyOutCount, x.GraceAppliedCount, x.MissingInCount, x.MissingOutCount, x.RegularizedDays, x.ApprovedOnDutyDays, x.LeaveConflictCount, x.ExceptionCount, x.ExpectedWorkMinutes, x.ActualWorkMinutes, x.SourceDataVersion, x.ProcessedAtUtc, x.PresentDayQuantity, x.PaidLeaveDays, x.UnpaidLeaveDays, x.PayableDays, x.LopDays, x.Version);
    private static AttendancePeriodOverviewDto Overview(AttendancePeriod p, IReadOnlyCollection<EmployeeAttendanceMonthlySummary> s, int blockingExceptions) => new(p.Id, s.Count, s.Count(x => x.ExceptionCount > 0), s.Sum(x => x.ExceptionCount), blockingExceptions, p.ProcessedAtUtc, p.Status, p.DataVersion);
}
