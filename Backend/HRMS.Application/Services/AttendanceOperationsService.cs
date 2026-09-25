using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Operational read/write facade over the existing Attendance engine. Exception facts are derived from
/// EmployeeAttendanceDay; corrections continue through Regularization, On Duty, AdminCorrection and the
/// monthly period services. This service never rewrites raw punches or finalized payroll snapshots.
/// </summary>
public sealed class AttendanceOperationsService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IAttendanceAuthorizationService? authorization = null,
    IAttendanceDayProcessor? processor = null,
    TimeProvider? timeProvider = null,
    IEmployeeSerializationLock? employeeLock = null) : IAttendanceOperationsService
{
    private const int MaxExportRows = 50_000;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<Result<PagedResult<AttendanceOperationalExceptionDto>>> GetMyExceptionsAsync(AttendanceOperationsExceptionQuery query, CancellationToken ct = default) =>
        GetExceptionsAsync(query, includeSelf: true, includeManager: false, includeRoleScope: false, Permissions.Attendance.View, ct);

    public Task<Result<PagedResult<AttendanceOperationalExceptionDto>>> GetOperationalExceptionsAsync(AttendanceOperationsExceptionQuery query, CancellationToken ct = default) =>
        GetExceptionsAsync(query, includeSelf: false, includeManager: true, includeRoleScope: true, Permissions.Attendance.ExceptionView, ct);

    public async Task<Result<AttendanceOperationsDashboardDto>> GetDashboardAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceOperationsDashboardDto>.Unauthorized("No authenticated tenant.");
        if (fromDate > toDate || toDate.DayNumber - fromDate.DayNumber + 1 > 3660) return Result<AttendanceOperationsDashboardDto>.Invalid("dateRange", "A valid date range of at most 3660 days is required.");
        var scope = await BuildScopeAsync(Permissions.Attendance.ExceptionView, fromDate, false, true, true, ct);
        if (!scope.Succeeded) return Result<AttendanceOperationsDashboardDto>.Failure(scope.Status, scope.Message, scope.Errors);
        var days = Days(tenantId, fromDate, toDate, scope.Value);
        var employees = await days.Select(x => x.EmployeeId).Distinct().CountAsync(ct);
        var processed = await days.CountAsync(ct);
        var present = await days.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.Present, ct);
        var absent = await days.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.Absent, ct);
        var leave = await days.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.OnLeave, ct);
        var onDuty = await days.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.OnDuty, ct);
        var weeklyOff = await days.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.WeeklyOff, ct);
        var holiday = await days.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.Holiday, ct);
        var exceptions = await days.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.Absent || x.Status == EmployeeAttendanceDayStatus.Incomplete || x.Status == EmployeeAttendanceDayStatus.NotProcessed || x.IsLateIn || x.IsEarlyOut || x.HasMissingInPunch || x.HasMissingOutPunch || x.HasInvalidPunchSequence || x.LeaveConflict, ct);
        var late = await CountOperationalExceptionsAsync(fromDate, toDate, AttendanceExceptionType.LateArrival, ct);
        var early = await CountOperationalExceptionsAsync(fromDate, toDate, AttendanceExceptionType.EarlyDeparture, ct);
        var pendingReg = 0;
        var correctionScope = await BuildScopeAsync(Permissions.Attendance.RegularizationApprove, fromDate, false, true, true, ct);
        if (correctionScope.Succeeded)
        {
            var pendingRegQuery = db.AttendanceRegularizationRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.BusinessDate >= fromDate && x.BusinessDate <= toDate && x.Status == AttendanceRequestStatus.Pending);
            if (correctionScope.Value is not null) pendingRegQuery = pendingRegQuery.Where(x => db.Employees.Where(correctionScope.Value).Any(e => e.TenantId == x.TenantId && e.Id == x.EmployeeId));
            pendingReg = await pendingRegQuery.CountAsync(ct);
        }
        var pendingOdQuery = db.AttendanceOnDutyRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.StartDate <= toDate && x.EndDate >= fromDate && x.Status == AttendanceRequestStatus.Pending);
        if (scope.Value is not null)
        {
            pendingOdQuery = pendingOdQuery.Where(x => db.Employees.Where(scope.Value).Any(e => e.Id == x.EmployeeId));
        }
        var pendingOd = await pendingOdQuery.CountAsync(ct);
        var activeAbsent = await CountOperationalExceptionsAsync(fromDate, toDate, AttendanceExceptionType.Absent, ct);
        var missedIn = await CountOperationalExceptionsAsync(fromDate, toDate, AttendanceExceptionType.MissingInPunch, ct);
        var missedOut = await CountOperationalExceptionsAsync(fromDate, toDate, AttendanceExceptionType.MissingOutPunch, ct);
        var missedPunch = missedIn + missedOut;
        var openPeriods = await db.AttendancePeriods.AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.StartDate <= toDate && x.EndDate >= fromDate && x.Status != AttendancePeriodStatus.Closed, ct);
        var finalizedPeriods = await db.AttendancePeriods.AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.StartDate <= toDate && x.EndDate >= fromDate && x.Status == AttendancePeriodStatus.Closed, ct);
        return Result<AttendanceOperationsDashboardDto>.Success(new(fromDate, toDate, employees, processed, present, absent, leave, onDuty, weeklyOff, holiday, exceptions, late, early, pendingReg, pendingOd, openPeriods, finalizedPeriods, activeAbsent, missedPunch, pendingReg, missedIn, missedOut));
    }

    public async Task<Result<RegularizationDto>> SubmitManualAttendanceAsync(ManualAttendanceRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out var userId)) return Result<RegularizationDto>.Unauthorized("An authenticated tenant and account are required.");
        if (request.EmployeeId == Guid.Empty) return Result<RegularizationDto>.Invalid("employeeId", "Employee is required.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result<RegularizationDto>.Invalid("reason", "A reason is required.");
        if (request.ExpectedAttendanceVersion < 1) return Result<RegularizationDto>.Invalid("expectedAttendanceVersion", "The expected Attendance version is required.");
        var scope = await BuildScopeAsync(Permissions.Attendance.AdminCorrectionManage, request.BusinessDate, false, true, true, ct);
        if (!scope.Succeeded || scope.Value is null) return Result<RegularizationDto>.Failure(scope.Status, scope.Message, scope.Errors);
        if (!await db.Employees.AsNoTracking().Where(scope.Value).AnyAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, ct)) return Result<RegularizationDto>.NotFound("The employee is outside the operator scope.");
        var period = await db.AttendancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.StartDate <= request.BusinessDate && x.EndDate >= request.BusinessDate, ct);
        if (period?.Status == AttendancePeriodStatus.Closed) return Result<RegularizationDto>.Conflict("The Attendance period is finalized. Reopen it before submitting a correction.");
        var currentVersion = period?.DataVersion ?? 1;
        if (currentVersion != request.ExpectedAttendanceVersion) return Result<RegularizationDto>.Conflict("AttendanceVersionConflict");
        await using var transaction = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (employeeLock is not null) await employeeLock.AcquireAsync(tenantId, request.EmployeeId, ct);
        if (await db.AttendanceRegularizationRequests.AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == request.EmployeeId && x.BusinessDate == request.BusinessDate && x.Status == AttendanceRequestStatus.Pending, ct)) return Result<RegularizationDto>.Conflict("A pending attendance correction already exists for this employee and date.");
        if (request.ProposedInAtUtc is null && request.ProposedOutAtUtc is null && request.CorrectionType == AttendanceRegularizationType.CorrectInOutTime) return Result<RegularizationDto>.Invalid("correction", "At least one corrected punch is required.");
        if (request.ProposedInAtUtc is DateTime input && request.ProposedOutAtUtc is DateTime output && output < input) return Result<RegularizationDto>.Invalid("proposedOutAtUtc", "Out time cannot precede in time.");
        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new AttendanceRegularizationRequest
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = request.EmployeeId, BusinessDate = request.BusinessDate,
            RequestType = request.CorrectionType, ProposedInAtUtc = request.ProposedInAtUtc, ProposedOutAtUtc = request.ProposedOutAtUtc,
            Reason = request.Reason.Trim(), SubmittedByUserId = userId, SubmittedAtUtc = now, Status = AttendanceRequestStatus.Pending
        };
        var employeeCode = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == request.EmployeeId).Select(x => x.EmployeeCode).SingleAsync(ct);
        db.AttendanceRegularizationRequests.Add(entity);
        db.AttendanceRegularizationEvents.Add(new AttendanceRegularizationEvent { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceRegularizationRequestId = entity.Id, EventType = AttendanceRequestEventType.Submitted, ActorUserId = userId, OccurredAtUtc = now, Comments = request.Comments?.Trim() });
        db.EmployeeAuditLogs.Add(new EmployeeAuditLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = request.EmployeeId, EmployeeCode = employeeCode,
            Module = "Attendance", Section = "Operations", EntityName = "AttendanceRegularizationRequest", RecordId = entity.Id,
            FieldName = "ManualAttendanceRequested", OldValue = null, NewValue = entity.Status.ToString(), ChangeType = AuditChangeType.Create,
            EffectiveDate = request.BusinessDate, ChangedBy = userId.ToString(), Reason = entity.Reason,
            Source = $"AttendanceVersion:{currentVersion}", ImportBatchId = entity.Id
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<RegularizationDto>.Success(new(entity.Id, entity.EmployeeId, entity.BusinessDate, entity.RequestType, entity.ProposedInAtUtc, entity.ProposedOutAtUtc, entity.Reason, entity.Status, entity.SubmittedAtUtc, null, null, []), "Manual Attendance request submitted for maker-checker approval.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RegularizationDto>.Conflict("A concurrent Attendance correction already changed this employee and date.");
        }
    }

    public async Task<Result<AttendanceBulkCorrectionResponse>> ApplyBulkCorrectionsAsync(IReadOnlyList<AttendanceBulkCorrectionItem> items, CancellationToken ct = default)
    {
        if (items is null || items.Count == 0) return Result<AttendanceBulkCorrectionResponse>.Invalid("items", "At least one item is required.");
        if (items.Count > 100) return Result<AttendanceBulkCorrectionResponse>.Invalid("items", "A bulk correction request cannot contain more than 100 items.");
        var results = new List<AttendanceBulkCorrectionResult>(items.Count);
        var completedItems = new Dictionary<AttendanceBulkCorrectionItem, AttendanceBulkCorrectionResult>();
        foreach (var item in items)
        {
            if (completedItems.TryGetValue(item, out var prior))
            {
                results.Add(prior with { Message = "Duplicate item is idempotently mapped to its original result." });
                continue;
            }
            var result = await SubmitManualAttendanceAsync(new(item.EmployeeId, item.BusinessDate, item.CorrectionType, item.ProposedInAtUtc, item.ProposedOutAtUtc, item.Reason, null, item.ExpectedAttendanceVersion), ct);
            if (result.Succeeded)
            {
                var success = new AttendanceBulkCorrectionResult(item.EmployeeId, item.BusinessDate, true, null, result.Message, result.Value!.Id, item.ExpectedAttendanceVersion);
                completedItems[item] = success;
                results.Add(success);
                continue;
            }

            var failureCode = result.Status switch
            {
                ResultStatus.Conflict when result.Message.Contains("finalized", StringComparison.OrdinalIgnoreCase) => "LockedPeriod",
                ResultStatus.Conflict when result.Message.Contains("Version", StringComparison.OrdinalIgnoreCase) => "StaleVersion",
                ResultStatus.Conflict when result.Message.Contains("pending attendance correction already exists", StringComparison.OrdinalIgnoreCase) => "AlreadyProcessed",
                ResultStatus.NotFound when result.Message.Contains("outside the operator scope", StringComparison.OrdinalIgnoreCase) => "Unauthorized",
                ResultStatus.NotFound => "NotFound",
                ResultStatus.Forbidden => "Unauthorized",
                ResultStatus.Conflict => "Conflict",
                ResultStatus.ValidationFailed => "InvalidCorrection",
                _ => "Failed"
            };
            var currentVersion = await db.AttendancePeriods.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.StartDate <= item.BusinessDate && x.EndDate >= item.BusinessDate).Select(x => (int?)x.DataVersion).FirstOrDefaultAsync(ct);
            var failure = new AttendanceBulkCorrectionResult(item.EmployeeId, item.BusinessDate, false, failureCode, result.Message, null, currentVersion);
            completedItems[item] = failure;
            results.Add(failure);
        }

        return Result<AttendanceBulkCorrectionResponse>.Success(new(results, results.Count(x => x.Success), results.Count(x => !x.Success)));
    }

    private async Task<int> CountOperationalExceptionsAsync(DateOnly fromDate, DateOnly toDate, AttendanceExceptionType type, CancellationToken ct)
    {
        var result = await GetOperationalExceptionsAsync(new AttendanceOperationsExceptionQuery
        {
            FromDate = fromDate, ToDate = toDate, ExceptionType = type, IsResolved = false, Page = 1, PageSize = 1
        }, ct);
        return result.Succeeded ? result.Value!.TotalCount : 0;
    }

    public async Task<Result<EmployeeAuditLogDto>> ResolveExceptionAsync(AttendanceExceptionResolutionRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId, out var userId)) return Result<EmployeeAuditLogDto>.Unauthorized("An authenticated tenant and account are required.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result<EmployeeAuditLogDto>.Invalid("reason", "A resolution reason is required.");
        if (!AttendanceExceptionResolutionSemantics.TryParseTerminal(request.Action, out var resolutionAction)) return Result<EmployeeAuditLogDto>.Invalid("action", "Action must be Acknowledge or Waive.");
        if (request.ExceptionType is not (AttendanceExceptionType.LateArrival or AttendanceExceptionType.EarlyDeparture or AttendanceExceptionType.Absent)) return Result<EmployeeAuditLogDto>.Invalid("exceptionType", "Only Late Arrival, Early Departure, and Absence can be acknowledged or waived.");
        var day = await db.EmployeeAttendanceDays.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AttendanceDayId, ct);
        if (day is null) return Result<EmployeeAuditLogDto>.NotFound("Attendance day was not found.");
        if (request.ExpectedAttendanceVersion < 1) return Result<EmployeeAuditLogDto>.Invalid("expectedAttendanceVersion", "Expected Attendance version is required.");
        var currentVersion = await db.AttendancePeriods.AsNoTracking().Where(x => x.TenantId == tenantId && x.StartDate <= day.BusinessDate && x.EndDate >= day.BusinessDate).Select(x => (int?)x.DataVersion).FirstOrDefaultAsync(ct) ?? 1;
        if (currentVersion != request.ExpectedAttendanceVersion) return Result<EmployeeAuditLogDto>.Conflict("AttendanceVersionConflict");
        var scope = await BuildScopeAsync(Permissions.Attendance.ExceptionView, day.BusinessDate, false, true, true, ct);
        if (!scope.Succeeded || scope.Value is null) return Result<EmployeeAuditLogDto>.Failure(scope.Status, scope.Message, scope.Errors);
        var employee = await db.Employees.AsNoTracking().Where(scope.Value).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == day.EmployeeId, ct);
        if (employee is null) return Result<EmployeeAuditLogDto>.NotFound("Attendance day is outside the operator scope.");
        var matches = request.ExceptionType switch
        {
            AttendanceExceptionType.LateArrival => day.IsLateIn,
            AttendanceExceptionType.EarlyDeparture => day.IsEarlyOut,
            AttendanceExceptionType.Absent => day.Status == EmployeeAttendanceDayStatus.Absent,
            _ => false
        };
        if (!matches) return Result<EmployeeAuditLogDto>.Conflict("The requested exception no longer exists on the authoritative Attendance day.");
        var audit = new EmployeeAuditLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = day.EmployeeId, EmployeeCode = employee.EmployeeCode,
            Module = "Attendance", Section = "Operations", EntityName = "EmployeeAttendanceDay", RecordId = day.Id,
            FieldName = $"{request.ExceptionType}:{request.Action}", OldValue = request.ExceptionType.ToString(), NewValue = request.Action,
            ChangeType = AuditChangeType.Update, EffectiveDate = day.BusinessDate, ChangedBy = userId.ToString(),
            Reason = request.Reason.Trim(), Source = $"AttendanceVersion:{currentVersion}"
        };
        var resolution = new AttendanceExceptionResolution
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = day.EmployeeId, AttendanceDayId = day.Id,
            AttendanceVersion = currentVersion, ExceptionType = request.ExceptionType,
            Action = resolutionAction, Reason = request.Reason.Trim(),
            ResolvedBy = userId, ResolvedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        db.AttendanceExceptionResolutions.Add(resolution);
        db.EmployeeAuditLogs.Add(audit);
        await db.SaveChangesAsync(ct);
        return Result<EmployeeAuditLogDto>.Success(new(audit.Id, audit.EmployeeId, audit.EmployeeCode, audit.Module, audit.Section, audit.EntityName, audit.RecordId, audit.FieldName, audit.OldValue, audit.NewValue, audit.ChangeType, audit.EffectiveDate, audit.ChangedBy, audit.Reason, audit.Source, audit.ImportBatchId, audit.IpAddress, audit.CreatedDate));
    }

    public async Task<Result<PagedResult<AttendanceOperationsHistoryItem>>> GetHistoryAsync(Guid employeeId, DateOnly? fromDate, DateOnly? toDate, PagedQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceOperationsHistoryItem>>.Unauthorized("No authenticated tenant.");
        if (employeeId == Guid.Empty || query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize || fromDate > toDate) return Result<PagedResult<AttendanceOperationsHistoryItem>>.Invalid("query", "The history query is invalid.");
        var scope = await BuildScopeAsync(Permissions.Attendance.ExceptionView, fromDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), true, true, true, ct);
        if (!scope.Succeeded || scope.Value is null) return Result<PagedResult<AttendanceOperationsHistoryItem>>.Failure(scope.Status, scope.Message, scope.Errors);
        var source = db.EmployeeAuditLogs.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Module == "Attendance");
        source = source.Where(x => db.Employees.Where(scope.Value).Any(e => e.TenantId == x.TenantId && e.Id == x.EmployeeId));
        if (fromDate is DateOnly from) source = source.Where(x => x.EffectiveDate >= from);
        if (toDate is DateOnly to) source = source.Where(x => x.EffectiveDate <= to);
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new { x.Id, x.EmployeeId, x.RecordId, x.EntityName, x.EffectiveDate, x.FieldName, x.Section, x.ChangedBy, x.CreatedDate, x.Reason, x.OldValue, x.NewValue, x.ImportBatchId, x.Source })
            .ToListAsync(ct);
        var auditItems = rows.Select(x =>
        {
            var versions = ParseAttendanceVersions(x.Source);
            return new AttendanceOperationsHistoryItem(x.Id, x.EmployeeId, x.EntityName == "EmployeeAttendanceDay" ? x.RecordId : null, x.EffectiveDate, x.FieldName ?? x.Section ?? "AttendanceChange", Guid.TryParse(x.ChangedBy, out var actor) ? actor : Guid.Empty, x.CreatedDate, x.Reason, x.OldValue, x.NewValue, x.ImportBatchId, versions.New ?? versions.Old, versions.Old, versions.New);
        }).ToList();
        var workflowSource = db.AttendanceRegularizationEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Request != null && e.Request.EmployeeId == employeeId)
            .Where(e => db.Employees.Where(scope.Value).Any(employee => employee.TenantId == e.TenantId && employee.Id == e.Request!.EmployeeId));
        if (fromDate is DateOnly eventFrom) workflowSource = workflowSource.Where(e => e.Request!.BusinessDate >= eventFrom);
        if (toDate is DateOnly eventTo) workflowSource = workflowSource.Where(e => e.Request!.BusinessDate <= eventTo);
        var workflowCount = await workflowSource.CountAsync(ct);
        var workflowRows = await workflowSource.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Take(query.Page * query.PageSize)
            .Select(e => new { e.Id, e.ActorUserId, e.OccurredAtUtc, e.Comments, e.EventType, e.AttendanceRegularizationRequestId, e.Request!.BusinessDate, e.Request.EmployeeId, e.Request.Reason })
            .ToListAsync(ct);
        var workflowItems = workflowRows.Select(e => new AttendanceOperationsHistoryItem(e.Id, e.EmployeeId, null, e.BusinessDate, $"Regularization:{e.EventType}", e.ActorUserId, e.OccurredAtUtc, e.Comments ?? e.Reason, null, e.EventType.ToString(), e.AttendanceRegularizationRequestId, null)).ToList();
        var allItems = auditItems.Concat(workflowItems).OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Result<PagedResult<AttendanceOperationsHistoryItem>>.Success(new(allItems, query.Page, query.PageSize, total + workflowCount));
    }

    public async Task<Result<AttendanceBulkActionResponse>> ApplyBulkActionAsync(IReadOnlyList<AttendanceBulkActionItem> items, CancellationToken ct = default)
    {
        if (items is null || items.Count == 0) return Result<AttendanceBulkActionResponse>.Invalid("items", "At least one item is required.");
        if (items.Count > 100) return Result<AttendanceBulkActionResponse>.Invalid("items", "A bulk request cannot contain more than 100 items.");
        if (!TryTenant(out var tenantId, out var userId)) return Result<AttendanceBulkActionResponse>.Unauthorized("An authenticated tenant and account are required.");
        var results = new List<AttendanceBulkActionResult>(items.Count);
        foreach (var item in items)
        {
            try
            {
                var result = item.IsOnDuty
                    ? await ReviewOnDutyAsync(item, tenantId, userId, ct)
                    : await ReviewRegularizationAsync(item, tenantId, userId, ct);
                results.Add(result);
            }
            catch (DbUpdateConcurrencyException)
            {
                results.Add(new(item.RequestId, item.IsOnDuty, false, "ConcurrencyConflict", "The request was changed by another operator.", item.ExpectedVersion + 1));
                db.ClearChangeTracker();
            }
            catch (DbUpdateException)
            {
                results.Add(new(item.RequestId, item.IsOnDuty, false, "PersistenceFailure", "The item was not changed.", null));
                db.ClearChangeTracker();
            }
        }
        return Result<AttendanceBulkActionResponse>.Success(new(results, results.Count(x => x.Success), results.Count(x => !x.Success)));
    }

    public async Task<Result<AttendanceOperationsExport>> ExportExceptionsAsync(AttendanceOperationsExceptionQuery query, CancellationToken ct = default)
    {
        var pageQuery = Clone(query, 1, PagedQuery.MaxPageSize);
        var first = await GetOperationalExceptionsAsync(pageQuery, ct);
        if (!first.Succeeded) return Result<AttendanceOperationsExport>.Failure(first.Status, first.Message, first.Errors);
        if (first.Value!.TotalCount > MaxExportRows) return Result<AttendanceOperationsExport>.Invalid("export", $"The export is limited to {MaxExportRows} rows. Narrow the filters and try again.");
        var rows = first.Value.Items.ToList();
        for (var page = 2; rows.Count < first.Value.TotalCount; page++)
        {
            var next = await GetOperationalExceptionsAsync(Clone(query, page, PagedQuery.MaxPageSize), ct);
            if (!next.Succeeded) return Result<AttendanceOperationsExport>.Failure(next.Status, next.Message, next.Errors);
            rows.AddRange(next.Value!.Items);
        }
        var csv = new CsvBuilder("Employee Code", "Employee Name", "Date", "Exception", "Status", "Shift", "In", "Out", "Worked Minutes", "Late Minutes", "Early Minutes", "Age Days", "Attendance Version", "Blocking", "Message");
        foreach (var x in rows) csv.AppendRow(x.EmployeeCode, x.EmployeeName, x.BusinessDate.ToString("yyyy-MM-dd"), x.ExceptionType.ToString(), x.AttendanceStatus.ToString(), x.ShiftCode, x.FirstPunchAtUtc?.ToString("O"), x.LastPunchAtUtc?.ToString("O"), x.WorkedMinutes?.ToString(), x.LateMinutes.ToString(), x.EarlyDepartureMinutes.ToString(), x.AgeDays.ToString(), x.AttendanceVersion.ToString(), x.IsBlocking.ToString(), x.Message);
        return Result<AttendanceOperationsExport>.Success(new("attendance-exceptions-operations.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount));
    }

    private async Task<Result<PagedResult<AttendanceOperationalExceptionDto>>> GetExceptionsAsync(AttendanceOperationsExceptionQuery query, bool includeSelf, bool includeManager, bool includeRoleScope, string permission, CancellationToken ct)
    {
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceOperationalExceptionDto>>.Unauthorized("No authenticated tenant.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize) return Result<PagedResult<AttendanceOperationalExceptionDto>>.Invalid("page", "Page values are out of range.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = query.FromDate ?? today.AddDays(-30);
        var to = query.ToDate ?? today;
        if (from > to || to.DayNumber - from.DayNumber + 1 > 3660) return Result<PagedResult<AttendanceOperationalExceptionDto>>.Invalid("dateRange", "A valid date range of at most 3660 days is required.");
        var scope = await BuildScopeAsync(permission, from, includeSelf, includeManager, includeRoleScope, ct);
        if (!scope.Succeeded) return Result<PagedResult<AttendanceOperationalExceptionDto>>.Failure(scope.Status, scope.Message, scope.Errors);
        var days = Days(tenantId, from, to, scope.Value);
        var source = days.Join(db.Employees.AsNoTracking(), day => new { day.TenantId, EmployeeId = day.EmployeeId }, employee => new { employee.TenantId, EmployeeId = employee.Id }, (day, employee) => new { day, employee });
        if (query.EmployeeId is Guid employeeId) source = source.Where(x => x.day.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(x => (x.employee.EmployeeCode != null && x.employee.EmployeeCode.Contains(search)) || (x.employee.FirstName + " " + x.employee.LastName).Contains(search));
        }
        if (query.AttendanceStatus is EmployeeAttendanceDayStatus status) source = source.Where(x => x.day.Status == status);
        source = query.ExceptionType switch
        {
            AttendanceExceptionType.MissingInPunch => source.Where(x => x.day.HasMissingInPunch),
            AttendanceExceptionType.MissingOutPunch => source.Where(x => x.day.HasMissingOutPunch),
            AttendanceExceptionType.Incomplete => source.Where(x => x.day.Status == EmployeeAttendanceDayStatus.Incomplete || x.day.HasInvalidPunchSequence),
            AttendanceExceptionType.NotProcessed => source.Where(x => x.day.Status == EmployeeAttendanceDayStatus.NotProcessed),
            AttendanceExceptionType.LeaveConflict => source.Where(x => x.day.LeaveConflict),
            AttendanceExceptionType.LateArrival => source.Where(x => x.day.IsLateIn),
            AttendanceExceptionType.EarlyDeparture => source.Where(x => x.day.IsEarlyOut),
            AttendanceExceptionType.Absent => source.Where(x => x.day.Status == EmployeeAttendanceDayStatus.Absent),
            _ => source.Where(x => x.day.Status == EmployeeAttendanceDayStatus.Absent || x.day.Status == EmployeeAttendanceDayStatus.Incomplete || x.day.Status == EmployeeAttendanceDayStatus.NotProcessed || x.day.IsLateIn || x.day.IsEarlyOut || x.day.HasMissingInPunch || x.day.HasMissingOutPunch || x.day.HasInvalidPunchSequence || x.day.LeaveConflict)
        };
        var activeResolutionMatches = source.Where(x => db.AttendanceExceptionResolutions.Any(a =>
            a.TenantId == tenantId && a.EmployeeId == x.day.EmployeeId && a.AttendanceDayId == x.day.Id &&
            a.AttendanceVersion == (db.AttendancePeriods.Where(p => p.TenantId == tenantId && p.StartDate <= x.day.BusinessDate && p.EndDate >= x.day.BusinessDate).Select(p => (int?)p.DataVersion).FirstOrDefault() ?? 1) &&
            a.ExceptionType == (query.ExceptionType ?? (x.day.HasMissingInPunch ? AttendanceExceptionType.MissingInPunch : x.day.HasMissingOutPunch ? AttendanceExceptionType.MissingOutPunch : x.day.Status == EmployeeAttendanceDayStatus.Absent ? AttendanceExceptionType.Absent : x.day.Status == EmployeeAttendanceDayStatus.NotProcessed ? AttendanceExceptionType.NotProcessed : x.day.Status == EmployeeAttendanceDayStatus.Incomplete || x.day.HasInvalidPunchSequence ? AttendanceExceptionType.Incomplete : x.day.IsLateIn ? AttendanceExceptionType.LateArrival : x.day.IsEarlyOut ? AttendanceExceptionType.EarlyDeparture : AttendanceExceptionType.LeaveConflict))));
        source = query.IsResolved is true ? activeResolutionMatches : source.Where(x => !db.AttendanceExceptionResolutions.Any(a =>
            a.TenantId == tenantId && a.EmployeeId == x.day.EmployeeId && a.AttendanceDayId == x.day.Id &&
            a.AttendanceVersion == (db.AttendancePeriods.Where(p => p.TenantId == tenantId && p.StartDate <= x.day.BusinessDate && p.EndDate >= x.day.BusinessDate).Select(p => (int?)p.DataVersion).FirstOrDefault() ?? 1) &&
            a.ExceptionType == (query.ExceptionType ?? (x.day.HasMissingInPunch ? AttendanceExceptionType.MissingInPunch : x.day.HasMissingOutPunch ? AttendanceExceptionType.MissingOutPunch : x.day.Status == EmployeeAttendanceDayStatus.Absent ? AttendanceExceptionType.Absent : x.day.Status == EmployeeAttendanceDayStatus.NotProcessed ? AttendanceExceptionType.NotProcessed : x.day.Status == EmployeeAttendanceDayStatus.Incomplete || x.day.HasInvalidPunchSequence ? AttendanceExceptionType.Incomplete : x.day.IsLateIn ? AttendanceExceptionType.LateArrival : x.day.IsEarlyOut ? AttendanceExceptionType.EarlyDeparture : AttendanceExceptionType.LeaveConflict))));
        var total = await source.CountAsync(ct);
        var raw = await source.OrderByDescending(x => x.day.BusinessDate).ThenBy(x => x.employee.EmployeeCode).ThenBy(x => x.day.EmployeeId).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => new OperationalDayRow(x.day.Id, x.day.EmployeeId, x.employee.EmployeeCode, (x.employee.FirstName + " " + x.employee.LastName).Trim(), x.day.BusinessDate, x.day.ShiftCode, x.day.Status, x.day.ScheduledStartUtc, x.day.ScheduledEndUtc, x.day.FirstPunchAtUtc, x.day.LastPunchAtUtc, x.day.WorkedMinutes, x.day.ExpectedWorkMinutes, x.day.IsLateIn, x.day.IsEarlyOut, x.day.HasMissingInPunch, x.day.HasMissingOutPunch, x.day.LeaveConflict, x.day.HasInvalidPunchSequence, db.AttendancePeriods.Where(p => p.TenantId == x.day.TenantId && p.StartDate <= x.day.BusinessDate && p.EndDate >= x.day.BusinessDate).Select(p => (int?)p.DataVersion).FirstOrDefault() ?? 1,
            db.AttendanceExceptionResolutions.Any(a => a.TenantId == tenantId && a.EmployeeId == x.day.EmployeeId && a.AttendanceDayId == x.day.Id && a.AttendanceVersion == (db.AttendancePeriods.Where(p => p.TenantId == tenantId && p.StartDate <= x.day.BusinessDate && p.EndDate >= x.day.BusinessDate).Select(p => (int?)p.DataVersion).FirstOrDefault() ?? 1) && a.ExceptionType == AttendanceExceptionType.LateArrival),
            db.AttendanceExceptionResolutions.Any(a => a.TenantId == tenantId && a.EmployeeId == x.day.EmployeeId && a.AttendanceDayId == x.day.Id && a.AttendanceVersion == (db.AttendancePeriods.Where(p => p.TenantId == tenantId && p.StartDate <= x.day.BusinessDate && p.EndDate >= x.day.BusinessDate).Select(p => (int?)p.DataVersion).FirstOrDefault() ?? 1) && a.ExceptionType == AttendanceExceptionType.EarlyDeparture),
            db.AttendanceExceptionResolutions.Any(a => a.TenantId == tenantId && a.EmployeeId == x.day.EmployeeId && a.AttendanceDayId == x.day.Id && a.AttendanceVersion == (db.AttendancePeriods.Where(p => p.TenantId == tenantId && p.StartDate <= x.day.BusinessDate && p.EndDate >= x.day.BusinessDate).Select(p => (int?)p.DataVersion).FirstOrDefault() ?? 1) && a.ExceptionType == AttendanceExceptionType.Absent))).ToListAsync(ct);
        var items = raw.Select(x => Map(x, query.ExceptionType, today)).ToList();
        return Result<PagedResult<AttendanceOperationalExceptionDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    private IQueryable<EmployeeAttendanceDay> Days(Guid tenantId, DateOnly from, DateOnly to, System.Linq.Expressions.Expression<Func<Employee, bool>>? scope)
    {
        var days = db.EmployeeAttendanceDays.AsNoTracking().Where(day => day.TenantId == tenantId && day.BusinessDate >= from && day.BusinessDate <= to);
        if (scope is not null) days = days.Where(day => db.Employees.Where(scope).Any(employee => employee.TenantId == day.TenantId && employee.Id == day.EmployeeId));
        return days;
    }

    private async Task<AttendanceBulkActionResult> ReviewRegularizationAsync(AttendanceBulkActionItem item, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var request = await db.AttendanceRegularizationRequests.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == item.RequestId, ct);
        if (request is null) return new(item.RequestId, false, false, "NotFound", "Regularization request was not found.", null);
        if (request.SubmittedByUserId == userId) return new(item.RequestId, false, false, "SelfApprovalDenied", "The maker cannot approve their own request.", request.ConcurrencyVersion);
        if (request.Status != AttendanceRequestStatus.Pending || request.ConcurrencyVersion != item.ExpectedVersion) return new(item.RequestId, false, false, "ConcurrencyConflict", "The request is stale or already processed.", request.ConcurrencyVersion);
        if (authorization is not null)
        {
            var access = await authorization.CanAccessEmployeeAsync(request.EmployeeId, Permissions.Attendance.RegularizationApprove, false, true, true, request.BusinessDate, ct);
            if (!access.Succeeded || access.Value != true) return new(item.RequestId, false, false, "Forbidden", "The request is outside the operator scope.", request.ConcurrencyVersion);
        }
        var oldDay = await db.EmployeeAttendanceDays.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == request.EmployeeId && x.BusinessDate == request.BusinessDate, ct);
        var oldAttendanceVersion = await db.AttendancePeriods.AsNoTracking().Where(x => x.TenantId == tenantId && x.StartDate <= request.BusinessDate && x.EndDate >= request.BusinessDate).Select(x => (int?)x.DataVersion).SingleOrDefaultAsync(ct) ?? 1;
        await using var transaction = await db.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        request.Status = item.Approve ? AttendanceRequestStatus.Approved : AttendanceRequestStatus.Rejected;
        request.ConcurrencyVersion++;
        request.ReviewedByUserId = userId;
        request.ReviewedAtUtc = now;
        request.ReviewerComments = item.Comments?.Trim();
        db.AttendanceRegularizationEvents.Add(new AttendanceRegularizationEvent { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceRegularizationRequestId = request.Id, EventType = item.Approve ? AttendanceRequestEventType.Approved : AttendanceRequestEventType.Rejected, ActorUserId = userId, OccurredAtUtc = now, Comments = item.Comments?.Trim() });
        if (item.Approve) db.AttendanceAdjustments.Add(new AttendanceAdjustment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = request.EmployeeId, BusinessDate = request.BusinessDate, AttendanceRegularizationRequestId = request.Id, EffectiveInAtUtc = request.ProposedInAtUtc, EffectiveOutAtUtc = request.ProposedOutAtUtc, ApprovedByUserId = userId, ApprovedAtUtc = now });
        await db.SaveChangesAsync(ct);
        if (item.Approve && processor is not null) { var processed = await processor.ProcessAsync(request.EmployeeId, request.BusinessDate, ct); if (!processed.Succeeded) return new(item.RequestId, false, false, "ReprocessFailed", processed.Message, request.ConcurrencyVersion); }
        var newDay = await db.EmployeeAttendanceDays.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == request.EmployeeId && x.BusinessDate == request.BusinessDate, ct);
        var newAttendanceVersion = await db.AttendancePeriods.AsNoTracking().Where(x => x.TenantId == tenantId && x.StartDate <= request.BusinessDate && x.EndDate >= request.BusinessDate).Select(x => (int?)x.DataVersion).SingleOrDefaultAsync(ct) ?? oldAttendanceVersion;
        var employeeCode = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == request.EmployeeId).Select(x => x.EmployeeCode).SingleAsync(ct);
        db.EmployeeAuditLogs.Add(new EmployeeAuditLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = request.EmployeeId, EmployeeCode = employeeCode,
            Module = "Attendance", Section = "Operations", EntityName = newDay is not null || oldDay is not null ? "EmployeeAttendanceDay" : "AttendanceRegularizationRequest",
            RecordId = newDay?.Id ?? oldDay?.Id ?? request.Id, FieldName = item.Approve ? "RegularizationApproved" : "RegularizationRejected",
            OldValue = oldDay?.Status.ToString(), NewValue = newDay?.Status.ToString(), ChangeType = AuditChangeType.Update,
            EffectiveDate = request.BusinessDate, ChangedBy = userId.ToString(), Reason = item.Comments?.Trim() ?? request.Reason,
            Source = $"AttendanceVersion:{oldAttendanceVersion}->{newAttendanceVersion}", ImportBatchId = request.Id
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(item.RequestId, false, true, null, item.Approve ? "Regularization approved." : "Regularization rejected.", request.ConcurrencyVersion);
    }

    private async Task<AttendanceBulkActionResult> ReviewOnDutyAsync(AttendanceBulkActionItem item, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var request = await db.AttendanceOnDutyRequests.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == item.RequestId, ct);
        if (request is null) return new(item.RequestId, true, false, "NotFound", "On Duty request was not found.", null);
        if (request.SubmittedByUserId == userId) return new(item.RequestId, true, false, "SelfApprovalDenied", "The maker cannot approve their own request.", request.ConcurrencyVersion);
        if (request.Status != AttendanceRequestStatus.Pending || request.ConcurrencyVersion != item.ExpectedVersion) return new(item.RequestId, true, false, "ConcurrencyConflict", "The request is stale or already processed.", request.ConcurrencyVersion);
        if (authorization is not null)
        {
            var access = await authorization.CanAccessEmployeeAsync(request.EmployeeId, Permissions.Attendance.OnDutyApprove, false, true, true, request.StartDate, ct);
            if (!access.Succeeded || access.Value != true) return new(item.RequestId, true, false, "Forbidden", "The request is outside the operator scope.", request.ConcurrencyVersion);
        }
        await using var transaction = await db.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        request.Status = item.Approve ? AttendanceRequestStatus.Approved : AttendanceRequestStatus.Rejected;
        request.ConcurrencyVersion++;
        request.ReviewedByUserId = userId;
        request.ReviewedAtUtc = now;
        request.ReviewerComments = item.Comments?.Trim();
        db.AttendanceOnDutyEvents.Add(new AttendanceOnDutyEvent { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceOnDutyRequestId = request.Id, EventType = item.Approve ? AttendanceRequestEventType.Approved : AttendanceRequestEventType.Rejected, ActorUserId = userId, OccurredAtUtc = now, Comments = item.Comments?.Trim() });
        await db.SaveChangesAsync(ct);
        if (item.Approve && processor is not null)
        {
            if (request.EndDate.DayNumber - request.StartDate.DayNumber > 31) return new(item.RequestId, true, false, "RangeTooLarge", "On Duty approval range is too large for a single operational action.", request.ConcurrencyVersion);
            for (var date = request.StartDate; date <= request.EndDate; date = date.AddDays(1)) { var processed = await processor.ProcessAsync(request.EmployeeId, date, ct); if (!processed.Succeeded) return new(item.RequestId, true, false, "ReprocessFailed", processed.Message, request.ConcurrencyVersion); }
        }
        await transaction.CommitAsync(ct);
        return new(item.RequestId, true, true, null, item.Approve ? "On Duty approved." : "On Duty rejected.", request.ConcurrencyVersion);
    }

    private async Task<Result<System.Linq.Expressions.Expression<Func<Employee, bool>>>> BuildScopeAsync(string permission, DateOnly date, bool self, bool manager, bool role, CancellationToken ct)
    {
        if (authorization is null) return Result<System.Linq.Expressions.Expression<Func<Employee, bool>>>.Success(x => true);
        return await authorization.BuildEmployeePredicateAsync(permission, self, manager, role, date, ct);
    }

    private static IQueryable<OperationalDayRow> ApplyExceptionFilter(IQueryable<OperationalDayRow> source, AttendanceExceptionType? type) => type switch
    {
        AttendanceExceptionType.MissingInPunch => source.Where(x => x.HasMissingInPunch),
        AttendanceExceptionType.MissingOutPunch => source.Where(x => x.HasMissingOutPunch),
        AttendanceExceptionType.Incomplete => source.Where(x => x.Status == EmployeeAttendanceDayStatus.Incomplete || x.HasInvalidPunchSequence),
        AttendanceExceptionType.NotProcessed => source.Where(x => x.Status == EmployeeAttendanceDayStatus.NotProcessed),
        AttendanceExceptionType.LeaveConflict => source.Where(x => x.LeaveConflict),
        AttendanceExceptionType.LateArrival => source.Where(x => x.IsLateIn),
        AttendanceExceptionType.EarlyDeparture => source.Where(x => x.IsEarlyOut),
        AttendanceExceptionType.Absent => source.Where(x => x.Status == EmployeeAttendanceDayStatus.Absent),
        _ => source.Where(ExceptionPredicate)
    };

    private static readonly System.Linq.Expressions.Expression<Func<OperationalDayRow, bool>> ExceptionPredicate = x =>
        x.Status == EmployeeAttendanceDayStatus.Absent || x.Status == EmployeeAttendanceDayStatus.Incomplete || x.Status == EmployeeAttendanceDayStatus.NotProcessed || x.IsLateIn || x.IsEarlyOut || x.HasMissingInPunch || x.HasMissingOutPunch || x.HasInvalidPunchSequence || x.LeaveConflict;
    private static AttendanceOperationalExceptionDto Map(OperationalDayRow x, AttendanceExceptionType? requested, DateOnly today)
    {
        var type = requested ?? (x.HasMissingInPunch ? AttendanceExceptionType.MissingInPunch : x.HasMissingOutPunch ? AttendanceExceptionType.MissingOutPunch : x.Status == EmployeeAttendanceDayStatus.Absent ? AttendanceExceptionType.Absent : x.Status == EmployeeAttendanceDayStatus.NotProcessed ? AttendanceExceptionType.NotProcessed : x.Status == EmployeeAttendanceDayStatus.Incomplete || x.HasInvalidPunchSequence ? AttendanceExceptionType.Incomplete : x.IsLateIn ? AttendanceExceptionType.LateArrival : x.IsEarlyOut ? AttendanceExceptionType.EarlyDeparture : AttendanceExceptionType.LeaveConflict);
        var late = x.ScheduledStartUtc is DateTime start && x.FirstPunchAtUtc is DateTime first && first > start ? Math.Max(0, (int)(first - start).TotalMinutes) : 0;
        var early = x.ScheduledEndUtc is DateTime end && x.LastPunchAtUtc is DateTime last && last < end ? Math.Max(0, (int)(end - last).TotalMinutes) : 0;
        var blocking = type is AttendanceExceptionType.MissingInPunch or AttendanceExceptionType.MissingOutPunch or AttendanceExceptionType.Incomplete or AttendanceExceptionType.NotProcessed or AttendanceExceptionType.Absent;
        var resolved = type == AttendanceExceptionType.LateArrival ? x.LateResolved : type == AttendanceExceptionType.EarlyDeparture ? x.EarlyResolved : type == AttendanceExceptionType.Absent && x.AbsenceResolved;
        return new(x.Id, x.EmployeeId, x.EmployeeCode, x.EmployeeName, x.BusinessDate, x.ShiftCode, type, x.Status, x.ScheduledStartUtc, x.ScheduledEndUtc, x.FirstPunchAtUtc, x.LastPunchAtUtc, x.WorkedMinutes, x.ExpectedWorkMinutes, late, early, blocking && !resolved, resolved, Math.Max(0, today.DayNumber - x.BusinessDate.DayNumber), x.AttendanceVersion, null, type == AttendanceExceptionType.LeaveConflict ? "Attendance conflicts with an approved source." : $"Attendance exception: {type}.");
    }

    private static (int? Old, int? New) ParseAttendanceVersions(string? source)
    {
        const string prefix = "AttendanceVersion:";
        if (source is null || !source.StartsWith(prefix, StringComparison.Ordinal)) return (null, null);
        var value = source[prefix.Length..];
        var parts = value.Split("->", StringSplitOptions.None);
        return parts.Length == 2 && int.TryParse(parts[0], out var oldVersion) && int.TryParse(parts[1], out var newVersion)
            ? (oldVersion, newVersion)
            : int.TryParse(value, out var version) ? (version, null) : (null, null);
    }

    private static AttendanceOperationsExceptionQuery Clone(AttendanceOperationsExceptionQuery q, int page, int pageSize) => new() { Page = page, PageSize = pageSize, Search = q.Search, SortBy = q.SortBy, SortDescending = q.SortDescending, FromDate = q.FromDate, ToDate = q.ToDate, EmployeeId = q.EmployeeId, ExceptionType = q.ExceptionType, AttendanceStatus = q.AttendanceStatus, IsResolved = q.IsResolved };
    private bool TryTenant(out Guid tenantId) { tenantId = tenant.TenantId ?? Guid.Empty; return tenantId != Guid.Empty; }
    private bool TryTenant(out Guid tenantId, out Guid userId) { tenantId = tenant.TenantId ?? Guid.Empty; userId = tenant.UserId ?? Guid.Empty; return tenantId != Guid.Empty && userId != Guid.Empty; }
    private sealed record OperationalDayRow(Guid Id, Guid EmployeeId, string? EmployeeCode, string EmployeeName, DateOnly BusinessDate, string? ShiftCode, EmployeeAttendanceDayStatus Status, DateTime? ScheduledStartUtc, DateTime? ScheduledEndUtc, DateTime? FirstPunchAtUtc, DateTime? LastPunchAtUtc, int? WorkedMinutes, int? ExpectedWorkMinutes, bool IsLateIn, bool IsEarlyOut, bool HasMissingInPunch, bool HasMissingOutPunch, bool LeaveConflict, bool HasInvalidPunchSequence, int AttendanceVersion, bool LateResolved, bool EarlyResolved, bool AbsenceResolved);
}
