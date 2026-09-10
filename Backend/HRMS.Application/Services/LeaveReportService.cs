using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveReportService : ILeaveReportService
{
    private const int MaxPageSize = 100;
    private const int MaxExportRows = 50_000;
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILeavePeriodResolver _periods;
    private readonly TimeProvider _clock;

    public LeaveReportService(IHrmsDbContext db, ITenantContext tenant, ILeavePeriodResolver periods, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _periods = periods;
        _clock = clock;
    }

    public async Task<Result<PagedResult<LeaveRequestReportRow>>> GetRequestsAsync(LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, cancellationToken);
        if (!scope.Succeeded) return Result<PagedResult<LeaveRequestReportRow>>.Failure(scope.Status, scope.Message, scope.Errors);
        var source = ApplyRequestFilters(RequestSource(scope.Value!.From, scope.Value.To), query);
        var total = await source.CountAsync(cancellationToken);
        var rows = await ProjectRequests(ApplyRequestSort(source, query)).Skip((scope.Value.Page - 1) * scope.Value.PageSize).Take(scope.Value.PageSize).ToListAsync(cancellationToken);
        return Result<PagedResult<LeaveRequestReportRow>>.Success(new(rows.Select(ToRequestRow).ToList(), scope.Value.Page, scope.Value.PageSize, total));
    }

    public Task<Result<PagedResult<LeaveRequestReportRow>>> GetEmployeeHistoryAsync(Guid employeeId, LeaveReportQuery query, CancellationToken cancellationToken = default) =>
        GetRequestsAsync(Clone(query, employeeId), cancellationToken);

    public async Task<Result<PagedResult<LeaveBalanceReportRow>>> GetBalancesAsync(LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var validation = ValidatePaging<LeaveBalanceReportRow>(query);
        if (validation is not null) return Result<PagedResult<LeaveBalanceReportRow>>.Failure(validation.Status, validation.Message, validation.Errors);
        if (!Tenant(out var tenantId)) return Result<PagedResult<LeaveBalanceReportRow>>.Unauthorized("No authenticated tenant.");
        var balances = await _db.EmployeeLeaveBalances.AsNoTracking()
            .Where(x => x.TenantId == tenantId && (!query.EmployeeId.HasValue || x.EmployeeId == query.EmployeeId) && (!query.LeaveTypeId.HasValue || x.LeaveTypeId == query.LeaveTypeId) && (!query.LeavePeriodId.HasValue || x.LeavePeriodId == query.LeavePeriodId))
            .Select(x => new BalanceRow(x.EmployeeId, x.Employee!.EmployeeCode ?? string.Empty, x.Employee.FirstName, x.Employee.MiddleName, x.Employee.LastName, x.LeaveType!.Name, x.LeaveTypeId, EntitlementMode.Allocated, x.GrantedQuantity, x.ReservedQuantity, x.ConsumedQuantity, x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity, x.LeavePeriodId))
            .ToListAsync(cancellationToken);
        var today = Today();
        var histories = await _db.EmployeeEmploymentHistory.AsNoTracking().Where(x => x.TenantId == tenantId && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today)).Select(x => new HistoryRow(x.EmployeeId, x.DepartmentId, x.Department != null ? x.Department.Name : x.DepartmentName, x.WorkLocationId, x.WorkLocation!.Name)).ToListAsync(cancellationToken);
        var historyByEmployee = histories.GroupBy(x => x.EmployeeId).ToDictionary(x => x.Key, x => x.First());
        var unlimitedIds = await (from rule in _db.LeavePolicyRules.AsNoTracking()
                                  join version in _db.LeavePolicyVersions.AsNoTracking() on rule.LeavePolicyVersionId equals version.Id
                                  join entitlement in _db.LeavePolicyEntitlementRules.AsNoTracking() on rule.Id equals entitlement.LeavePolicyRuleId
                                  where rule.TenantId == tenantId && rule.IsActive && version.Status == LeavePolicyVersionStatus.Published && version.EffectiveFrom <= today && (version.EffectiveTo == null || version.EffectiveTo >= today) && entitlement.EntitlementMode == EntitlementMode.Unlimited
                                  select rule.LeaveTypeId).Distinct().ToListAsync(cancellationToken);
        var result = balances.Select(x => ToBalanceRow(x, historyByEmployee)).ToList();
        var unlimitedTypes = await _db.LeaveTypes.AsNoTracking().Where(x => x.TenantId == tenantId && unlimitedIds.Contains(x.Id) && (!query.LeaveTypeId.HasValue || query.LeaveTypeId == x.Id)).ToListAsync(cancellationToken);
        foreach (var type in unlimitedTypes.Where(x => result.All(r => r.LeaveType == x.Name)))
            result.Add(new(Guid.Empty, string.Empty, string.Empty, null, null, type.Name, EntitlementMode.Unlimited, null, null, null, null, null, null));
        result = result.Where(x => !query.DepartmentId.HasValue || histories.Any(h => h.EmployeeId == x.EmployeeId && h.DepartmentId == query.DepartmentId)).Where(x => !query.WorkLocationId.HasValue || histories.Any(h => h.EmployeeId == x.EmployeeId && h.WorkLocationId == query.WorkLocationId)).OrderBy(x => x.EmployeeCode).ThenBy(x => x.LeaveType).ToList();
        var total = result.Count;
        return Result<PagedResult<LeaveBalanceReportRow>>.Success(new(result.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<LeaveUsageReportRow>>> GetUsageAsync(LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, cancellationToken);
        if (!scope.Succeeded) return Result<IReadOnlyList<LeaveUsageReportRow>>.Failure(scope.Status, scope.Message, scope.Errors);
        var rows = await ProjectRequests(ApplyRequestFilters(RequestSource(scope.Value!.From, scope.Value.To), query).Where(x => x.Status == LeaveRequestStatus.Approved)).ToListAsync(cancellationToken);
        var result = rows.GroupBy(x => new { x.LeaveTypeId, x.LeaveTypeName }).Select(g => new LeaveUsageReportRow("Leave Type", g.Key.LeaveTypeName, g.Count(), g.Select(x => x.EmployeeId).Distinct().Count(), g.Sum(x => x.Quantity))).OrderByDescending(x => x.Quantity).ToList();
        return Result<IReadOnlyList<LeaveUsageReportRow>>.Success(result);
    }

    public async Task<Result<PagedResult<LeaveAccountingReportRow>>> GetAccountingAsync(LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, cancellationToken);
        if (!scope.Succeeded) return Result<PagedResult<LeaveAccountingReportRow>>.Failure(scope.Status, scope.Message, scope.Errors);
        var tenantId = _tenant.TenantId!.Value;
        var transactions = await (from x in _db.LeaveBalanceTransactions.AsNoTracking()
                                  where x.TenantId == tenantId && x.EffectiveDate >= scope.Value!.From && x.EffectiveDate <= scope.Value.To && (x.TransactionType == LeaveBalanceTransactionType.Accrual || x.TransactionType == LeaveBalanceTransactionType.CarryForward || x.TransactionType == LeaveBalanceTransactionType.Expiry) && (!query.EmployeeId.HasValue || x.EmployeeId == query.EmployeeId) && (!query.LeaveTypeId.HasValue || x.LeaveTypeId == query.LeaveTypeId)
                                  select new LeaveAccountingReportRow(x.EmployeeId, x.Employee!.EmployeeCode ?? string.Empty, x.Employee.FirstName + " " + x.Employee.LastName, x.LeaveType!.Name, x.EmployeeLeaveBalance!.LeavePeriod!.Name, x.EffectiveDate, x.TransactionType.ToString(), x.Quantity, x.SourceType, x.SourceReference, null, null, null)).ToListAsync(cancellationToken);
        var occurrences = await _db.LeaveAccrualOccurrences.AsNoTracking().Where(x => x.TenantId == tenantId && x.OccurrenceDate >= scope.Value!.From && x.OccurrenceDate <= scope.Value.To && (!query.EmployeeId.HasValue || x.EmployeeId == query.EmployeeId) && (!query.LeaveTypeId.HasValue || x.LeaveTypeId == query.LeaveTypeId)).Select(x => new LeaveAccountingReportRow(x.EmployeeId, x.Employee!.EmployeeCode ?? string.Empty, x.Employee.FirstName + " " + x.Employee.LastName, x.LeaveType!.Name, x.LeavePeriod!.Name, x.OccurrenceDate, "Accrual occurrence", x.CreditedQuantity, LeaveBalanceSourceType.Policy, x.OccurrenceKey, x.CalculatedQuantity, x.CreditedQuantity, null)).ToListAsync(cancellationToken);
        var closeRows = await _db.LeavePeriodCloseOccurrences.AsNoTracking().Where(x => x.TenantId == tenantId && (!query.EmployeeId.HasValue || x.EmployeeId == query.EmployeeId) && (!query.LeaveTypeId.HasValue || x.LeaveTypeId == query.LeaveTypeId)).Select(x => new { x.EmployeeId, x.LeaveTypeId, x.DestinationLeavePeriodId, x.SourceLeavePeriodId, x.CarriedQuantity, x.LapsedQuantity, x.OccurrenceKey }).ToListAsync(cancellationToken);
        var closeDates = await _db.LeavePeriods.AsNoTracking().Where(x => x.TenantId == tenantId && closeRows.Select(c => c.DestinationLeavePeriodId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.StartDate, cancellationToken);
        var closeTypes = await _db.LeaveTypes.AsNoTracking().Where(x => x.TenantId == tenantId && closeRows.Select(c => c.LeaveTypeId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var closeEmployees = await _db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && closeRows.Select(c => c.EmployeeId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => new { Code = x.EmployeeCode ?? string.Empty, Name = x.FirstName + " " + x.LastName }, cancellationToken);
        var carryRows = closeRows.Select(x => new LeaveAccountingReportRow(x.EmployeeId, closeEmployees[x.EmployeeId].Code, closeEmployees[x.EmployeeId].Name, closeTypes[x.LeaveTypeId], string.Empty, closeDates.GetValueOrDefault(x.DestinationLeavePeriodId, Today()), "CarryForward", x.CarriedQuantity, LeaveBalanceSourceType.CarryForward, x.OccurrenceKey, null, x.CarriedQuantity, x.LapsedQuantity)).Where(x => x.Date >= scope.Value!.From && x.Date <= scope.Value.To);
        var all = transactions.Concat(occurrences).Concat(carryRows).OrderByDescending(x => x.Date).ThenBy(x => x.EmployeeCode).ToList();
        var total = all.Count;
        return Result<PagedResult<LeaveAccountingReportRow>>.Success(new(all.Skip((scope.Value.Page - 1) * scope.Value.PageSize).Take(scope.Value.PageSize).ToList(), scope.Value.Page, scope.Value.PageSize, total));
    }

    public async Task<Result<PagedResult<PendingApprovalReportRow>>> GetPendingAsync(LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, cancellationToken);
        if (!scope.Succeeded) return Result<PagedResult<PendingApprovalReportRow>>.Failure(scope.Status, scope.Message, scope.Errors);
        var rows = await ProjectRequests(ApplyRequestFilters(RequestSource(scope.Value!.From, scope.Value.To), query).Where(x => x.Status == LeaveRequestStatus.PendingApproval).OrderBy(x => x.SubmittedAtUtc)).ToListAsync(cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime.Date;
        var result = rows.Select(x => { var days = x.SubmittedAtUtc.HasValue ? Math.Max(0, (now - x.SubmittedAtUtc.Value.Date).Days) : 0; return new PendingApprovalReportRow(x.Id, x.EmployeeCode, x.EmployeeName, x.LeaveTypeName, x.StartDate, x.EndDate, x.Quantity, x.SubmittedAtUtc, days, x.ManagerName, x.DepartmentName, x.WorkLocationName, days <= 1 ? "0-1 day" : days <= 3 ? "2-3 days" : days <= 7 ? "4-7 days" : "8+ days"); }).ToList();
        var total = result.Count;
        return Result<PagedResult<PendingApprovalReportRow>>.Success(new(result.Skip((scope.Value.Page - 1) * scope.Value.PageSize).Take(scope.Value.PageSize).ToList(), scope.Value.Page, scope.Value.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<LeaveOrganizationReportRow>>> GetOrganizationAsync(LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, cancellationToken);
        if (!scope.Succeeded) return Result<IReadOnlyList<LeaveOrganizationReportRow>>.Failure(scope.Status, scope.Message, scope.Errors);
        var rows = await ProjectRequests(ApplyRequestFilters(RequestSource(scope.Value!.From, scope.Value.To), query)).ToListAsync(cancellationToken);
        var useWorkLocation = string.Equals(query.OrganizationDimension, "workLocation", StringComparison.OrdinalIgnoreCase);
        var grouped = rows.GroupBy(x => (useWorkLocation ? x.WorkLocationName : x.DepartmentName) ?? "Unassigned").Select(g => new LeaveOrganizationReportRow(g.Key, g.Where(x => x.Status == LeaveRequestStatus.Approved).Select(x => x.EmployeeId).Distinct().Count(), g.Count(), g.Count(x => x.Status == LeaveRequestStatus.PendingApproval), g.Where(x => x.Status == LeaveRequestStatus.Approved).Sum(x => x.Quantity))).OrderByDescending(x => x.ApprovedQuantity).ToList();
        return Result<IReadOnlyList<LeaveOrganizationReportRow>>.Success(grouped);
    }

    public async Task<Result<IReadOnlyList<LeaveCalendarReportRow>>> GetCalendarAsync(LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, cancellationToken);
        if (!scope.Succeeded) return Result<IReadOnlyList<LeaveCalendarReportRow>>.Failure(scope.Status, scope.Message, scope.Errors);
        var tenantId = _tenant.TenantId!.Value;
        var holidays = await _db.Holidays.AsNoTracking().Where(x => x.TenantId == tenantId && x.Date >= scope.Value!.From && x.Date <= scope.Value.To && (!query.WorkLocationId.HasValue || x.WorkLocationId == null || x.WorkLocationId == query.WorkLocationId) && (!query.IsActiveOnly() || x.IsActive)).Select(x => new LeaveCalendarReportRow("Holiday", x.Name, x.Date, x.Date, x.Date, x.CountryLocationId, x.WorkLocationId, null, x.IsActive)).ToListAsync(cancellationToken);
        var weekly = await _db.WeeklyOffConfigurations.AsNoTracking().Where(x => x.TenantId == tenantId && x.EffectiveFrom <= scope.Value!.To && (x.EffectiveTo == null || x.EffectiveTo >= scope.Value.From) && (!query.WorkLocationId.HasValue || x.WorkLocationId == null || x.WorkLocationId == query.WorkLocationId) && (!query.IsActiveOnly() || x.IsActive)).Include(x => x.Days).ToListAsync(cancellationToken);
        var weeklyRows = weekly.Select(x => new LeaveCalendarReportRow("WeeklyOff", "Weekly Off", null, x.EffectiveFrom, x.EffectiveTo, x.CountryLocationId, x.WorkLocationId, string.Join(", ", x.Days.OrderBy(d => d.DayOfWeek).Select(d => d.DayOfWeek)), x.IsActive));
        return Result<IReadOnlyList<LeaveCalendarReportRow>>.Success(holidays.Concat(weeklyRows).OrderBy(x => x.Date ?? x.EffectiveFrom).ToList());
    }

    public async Task<Result<LeaveReportExportDto>> ExportCsvAsync(string report, LeaveReportQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = report.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "requests": return await ExportRequestsAsync(query, cancellationToken);
            case "balances": return await ExportBalancesAsync(query, cancellationToken);
            case "pending": return await ExportPendingAsync(query, cancellationToken);
            case "accounting": return await ExportAccountingAsync(query, cancellationToken);
            case "usage": return await ExportUsageAsync(query, cancellationToken);
            case "organization": return await ExportOrganizationAsync(query, cancellationToken);
            case "calendar": return await ExportCalendarAsync(query, cancellationToken);
            default: return Result<LeaveReportExportDto>.Invalid("report", "The requested report export is not supported.");
        }
    }

    private async Task<Result<LeaveReportExportDto>> ExportRequestsAsync(LeaveReportQuery query, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(query, ct); if (!scope.Succeeded) return Result<LeaveReportExportDto>.Failure(scope.Status, scope.Message, scope.Errors);
        var source = ApplyRequestFilters(RequestSource(scope.Value!.From, scope.Value.To), query); var total = await source.CountAsync(ct); if (total > MaxExportRows) return Result<LeaveReportExportDto>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again.");
        var csv = new CsvBuilder("Request Id", "Employee Code", "Employee Name", "Leave Type", "Start Date", "End Date", "Quantity", "Status", "Submitted Date", "Department", "WorkLocation", "Manager", "Leave Period");
        foreach (var row in await ProjectRequests(ApplyRequestSort(source, query)).Take(MaxExportRows).ToListAsync(ct)) { var x = ToRequestRow(row); csv.AppendRow(x.RequestId.ToString("D"), x.EmployeeCode, x.EmployeeName, x.LeaveType, x.StartDate.ToString("yyyy-MM-dd"), x.EndDate.ToString("yyyy-MM-dd"), x.Quantity.ToString(), x.Status.ToString(), x.SubmittedAtUtc?.ToString("O"), x.Department, x.WorkLocation, x.Manager, x.LeavePeriod); }
        return Result<LeaveReportExportDto>.Success(new($"leave-requests-{scope.Value.From:yyyy-MM-dd}-to-{scope.Value.To:yyyy-MM-dd}.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount));
    }

    private async Task<Result<LeaveReportExportDto>> ExportBalancesAsync(LeaveReportQuery query, CancellationToken ct) { var result = await GetAllBalancesAsync(query, ct); if (!result.Succeeded) return Result<LeaveReportExportDto>.Failure(result.Status, result.Message, result.Errors); var csv = new CsvBuilder("Employee Code", "Employee Name", "Department", "WorkLocation", "Leave Type", "Entitlement Mode", "Granted", "Reserved", "Consumed", "Available", "Carry Forward", "Expiring Carry Forward"); foreach (var x in result.Value!) csv.AppendRow(x.EmployeeCode, x.EmployeeName, x.Department, x.WorkLocation, x.LeaveType, x.EntitlementMode.ToString(), x.Granted?.ToString(), x.Reserved?.ToString(), x.Consumed?.ToString(), x.Available?.ToString(), x.CarryForward?.ToString(), x.ExpiringCarryForward?.ToString()); return Result<LeaveReportExportDto>.Success(new("leave-balances.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount)); }
    private async Task<Result<LeaveReportExportDto>> ExportPendingAsync(LeaveReportQuery query, CancellationToken ct) { var result = await GetAllPendingAsync(query, ct); if (!result.Succeeded) return Result<LeaveReportExportDto>.Failure(result.Status, result.Message, result.Errors); var csv = new CsvBuilder("Request Id", "Employee Code", "Employee Name", "Leave Type", "Start Date", "End Date", "Quantity", "Pending Since", "Days Pending", "Aging Bucket", "Manager", "Department", "WorkLocation"); foreach (var x in result.Value!) csv.AppendRow(x.RequestId.ToString("D"), x.EmployeeCode, x.EmployeeName, x.LeaveType, x.StartDate.ToString("yyyy-MM-dd"), x.EndDate.ToString("yyyy-MM-dd"), x.Quantity.ToString(), x.PendingSinceUtc?.ToString("O"), x.DaysPending.ToString(), x.AgingBucket, x.Manager, x.Department, x.WorkLocation); return Result<LeaveReportExportDto>.Success(new("pending-approval-aging.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount)); }
    private async Task<Result<LeaveReportExportDto>> ExportAccountingAsync(LeaveReportQuery query, CancellationToken ct) { var result = await GetAllAccountingAsync(query, ct); if (!result.Succeeded) return Result<LeaveReportExportDto>.Failure(result.Status, result.Message, result.Errors); var csv = new CsvBuilder("Employee Code", "Employee Name", "Leave Type", "Leave Period", "Date", "Event Type", "Quantity", "Source Type", "Source Reference", "Calculated", "Credited", "Lapsed"); foreach (var x in result.Value!) csv.AppendRow(x.EmployeeCode, x.EmployeeName, x.LeaveType, x.LeavePeriod, x.Date.ToString("yyyy-MM-dd"), x.EventType, x.Quantity.ToString(), x.SourceType.ToString(), x.SourceReference, x.CalculatedQuantity?.ToString(), x.CreditedQuantity?.ToString(), x.LapsedQuantity?.ToString()); return Result<LeaveReportExportDto>.Success(new("leave-accrual-carry-forward.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount)); }
    private async Task<Result<LeaveReportExportDto>> ExportUsageAsync(LeaveReportQuery query, CancellationToken ct) { var result = await GetUsageAsync(query, ct); if (!result.Succeeded) return Result<LeaveReportExportDto>.Failure(result.Status, result.Message, result.Errors); if (result.Value!.Count > MaxExportRows) return Result<LeaveReportExportDto>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again."); var csv = new CsvBuilder("Group", "Leave Type", "Approved Requests", "Employees", "Quantity"); foreach (var x in result.Value) csv.AppendRow(x.Group, x.LeaveType, x.RequestCount.ToString(), x.EmployeeCount.ToString(), x.Quantity.ToString()); return Result<LeaveReportExportDto>.Success(new("leave-utilization.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount)); }
    private async Task<Result<LeaveReportExportDto>> ExportOrganizationAsync(LeaveReportQuery query, CancellationToken ct) { var result = await GetOrganizationAsync(query, ct); if (!result.Succeeded) return Result<LeaveReportExportDto>.Failure(result.Status, result.Message, result.Errors); if (result.Value!.Count > MaxExportRows) return Result<LeaveReportExportDto>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again."); var csv = new CsvBuilder("Group", "Employees", "Requests", "Pending", "Approved Quantity"); foreach (var x in result.Value) csv.AppendRow(x.Group, x.EmployeeCount.ToString(), x.RequestCount.ToString(), x.PendingCount.ToString(), x.ApprovedQuantity.ToString()); return Result<LeaveReportExportDto>.Success(new("leave-organization-summary.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount)); }
    private async Task<Result<LeaveReportExportDto>> ExportCalendarAsync(LeaveReportQuery query, CancellationToken ct) { var result = await GetCalendarAsync(query, ct); if (!result.Succeeded) return Result<LeaveReportExportDto>.Failure(result.Status, result.Message, result.Errors); if (result.Value!.Count > MaxExportRows) return Result<LeaveReportExportDto>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again."); var csv = new CsvBuilder("Kind", "Name", "Date", "Effective From", "Effective To", "Country Id", "WorkLocation Id", "Weekdays", "Active"); foreach (var x in result.Value) csv.AppendRow(x.Kind, x.Name, x.Date?.ToString("yyyy-MM-dd"), x.EffectiveFrom.ToString("yyyy-MM-dd"), x.EffectiveTo?.ToString("yyyy-MM-dd"), x.CountryId?.ToString("D"), x.WorkLocationId?.ToString("D"), x.Weekdays, x.IsActive.ToString()); return Result<LeaveReportExportDto>.Success(new("leave-holiday-calendar.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount)); }

    private IQueryable<LeaveRequest> RequestSource(DateOnly from, DateOnly to) => _db.LeaveRequests.AsNoTracking().Where(x => x.TenantId == _tenant.TenantId!.Value && x.StartDate <= to && x.EndDate >= from);
    private static IQueryable<LeaveRequest> ApplyRequestFilters(IQueryable<LeaveRequest> source, LeaveReportQuery query) { if (query.EmployeeId is Guid employee) source = source.Where(x => x.EmployeeId == employee); if (query.LeaveTypeId is Guid type) source = source.Where(x => x.LeaveTypeId == type); if (query.Status is LeaveRequestStatus status) source = source.Where(x => x.Status == status); if (query.DepartmentId is Guid department) source = source.Where(x => x.EmployeeEmploymentHistory!.DepartmentId == department); if (query.WorkLocationId is Guid location) source = source.Where(x => x.EmployeeEmploymentHistory!.WorkLocationId == location); return source; }
    private static IQueryable<LeaveRequest> ApplyRequestSort(IQueryable<LeaveRequest> source, LeaveReportQuery query) => query.SortBy?.ToLowerInvariant() switch { "employeecode" => query.Descending ? source.OrderByDescending(x => x.Employee!.EmployeeCode) : source.OrderBy(x => x.Employee!.EmployeeCode), "employee" => query.Descending ? source.OrderByDescending(x => x.Employee!.FirstName).ThenByDescending(x => x.Employee!.LastName) : source.OrderBy(x => x.Employee!.FirstName).ThenBy(x => x.Employee!.LastName), "leavetype" => query.Descending ? source.OrderByDescending(x => x.LeaveType!.Name) : source.OrderBy(x => x.LeaveType!.Name), "status" => query.Descending ? source.OrderByDescending(x => x.Status) : source.OrderBy(x => x.Status), "startdate" => query.Descending ? source.OrderByDescending(x => x.StartDate) : source.OrderBy(x => x.StartDate), _ => source.OrderByDescending(x => x.StartDate).ThenBy(x => x.Employee!.EmployeeCode) };
    private static IQueryable<RequestRow> ProjectRequests(IQueryable<LeaveRequest> source) => source.Select(x => new RequestRow(x.Id, x.EmployeeId, x.Status, x.StartDate, x.EndDate, x.ChargeableQuantity, x.SubmittedAtUtc, x.LeaveTypeId, x.LeaveType!.Name, x.Employee!.EmployeeCode ?? string.Empty, x.Employee.FirstName, x.Employee.MiddleName, x.Employee.LastName, x.EmployeeEmploymentHistory!.DepartmentId, x.EmployeeEmploymentHistory.Department != null ? x.EmployeeEmploymentHistory.Department.Name : x.EmployeeEmploymentHistory.DepartmentName, x.EmployeeEmploymentHistory.WorkLocationId, x.EmployeeEmploymentHistory.WorkLocation!.Name, x.EmployeeEmploymentHistory.ManagerName, x.LeavePeriod!.Name));
    private async Task<Result<(DateOnly From, DateOnly To, int Page, int PageSize)>> ResolveScopeAsync(LeaveReportQuery query, CancellationToken ct) { var paging = ValidatePaging<LeaveRequestReportRow>(query); if (paging is not null) return Result<(DateOnly, DateOnly, int, int)>.Failure(paging.Status, paging.Message, paging.Errors); if (!Tenant(out var tenantId)) return Result<(DateOnly, DateOnly, int, int)>.Unauthorized("No authenticated tenant."); var today = Today(); var period = await _periods.ResolveAsync(tenantId, today, ct); var from = query.FromDate ?? period.Period?.StartDate ?? today.AddDays(-30); var to = query.ToDate ?? period.Period?.EndDate ?? today.AddDays(30); if (to < from) return Result<(DateOnly, DateOnly, int, int)>.Invalid("to", "ToDate must not be before FromDate."); if (to.DayNumber - from.DayNumber > 366) return Result<(DateOnly, DateOnly, int, int)>.Invalid("to", "The interactive report range cannot exceed 366 days."); return Result<(DateOnly, DateOnly, int, int)>.Success((from, to, query.Page, query.PageSize)); }
    private static Result<PagedResult<T>>? ValidatePaging<T>(LeaveReportQuery query) { if (query.Page < 1 || query.PageSize is < 1 or > MaxPageSize) return Result<PagedResult<T>>.Invalid("page", $"Page must be at least 1 and page size must be between 1 and {MaxPageSize}."); return null; }
    private async Task<Result<IReadOnlyList<LeaveBalanceReportRow>>> GetAllBalancesAsync(LeaveReportQuery query, CancellationToken ct) { var rows = new List<LeaveBalanceReportRow>(); for (var page = 1; ; page++) { var result = await GetBalancesAsync(new LeaveReportQuery { EmployeeId = query.EmployeeId, LeaveTypeId = query.LeaveTypeId, LeavePeriodId = query.LeavePeriodId, DepartmentId = query.DepartmentId, WorkLocationId = query.WorkLocationId, Page = page, PageSize = MaxPageSize }, ct); if (!result.Succeeded) return Result<IReadOnlyList<LeaveBalanceReportRow>>.Failure(result.Status, result.Message, result.Errors); rows.AddRange(result.Value!.Items); if (rows.Count > MaxExportRows) return Result<IReadOnlyList<LeaveBalanceReportRow>>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again."); if (!result.Value.HasNextPage) break; } return Result<IReadOnlyList<LeaveBalanceReportRow>>.Success(rows); }
    private async Task<Result<IReadOnlyList<PendingApprovalReportRow>>> GetAllPendingAsync(LeaveReportQuery query, CancellationToken ct) { var rows = new List<PendingApprovalReportRow>(); for (var page = 1; ; page++) { var result = await GetPendingAsync(new LeaveReportQuery { FromDate = query.FromDate, ToDate = query.ToDate, LeaveTypeId = query.LeaveTypeId, DepartmentId = query.DepartmentId, WorkLocationId = query.WorkLocationId, Page = page, PageSize = MaxPageSize }, ct); if (!result.Succeeded) return Result<IReadOnlyList<PendingApprovalReportRow>>.Failure(result.Status, result.Message, result.Errors); rows.AddRange(result.Value!.Items); if (rows.Count > MaxExportRows) return Result<IReadOnlyList<PendingApprovalReportRow>>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again."); if (!result.Value.HasNextPage) break; } return Result<IReadOnlyList<PendingApprovalReportRow>>.Success(rows); }
    private async Task<Result<IReadOnlyList<LeaveAccountingReportRow>>> GetAllAccountingAsync(LeaveReportQuery query, CancellationToken ct) { var rows = new List<LeaveAccountingReportRow>(); for (var page = 1; ; page++) { var result = await GetAccountingAsync(new LeaveReportQuery { FromDate = query.FromDate, ToDate = query.ToDate, EmployeeId = query.EmployeeId, LeaveTypeId = query.LeaveTypeId, Page = page, PageSize = MaxPageSize }, ct); if (!result.Succeeded) return Result<IReadOnlyList<LeaveAccountingReportRow>>.Failure(result.Status, result.Message, result.Errors); rows.AddRange(result.Value!.Items); if (rows.Count > MaxExportRows) return Result<IReadOnlyList<LeaveAccountingReportRow>>.Invalid("export", $"The report contains more than {MaxExportRows} rows. Narrow the filters and try again."); if (!result.Value.HasNextPage) break; } return Result<IReadOnlyList<LeaveAccountingReportRow>>.Success(rows); }
    private static LeaveReportQuery Clone(LeaveReportQuery q, Guid employeeId) => new() { FromDate = q.FromDate, ToDate = q.ToDate, LeaveTypeId = q.LeaveTypeId, LeavePeriodId = q.LeavePeriodId, Status = q.Status, DepartmentId = q.DepartmentId, WorkLocationId = q.WorkLocationId, OrganizationDimension = q.OrganizationDimension, SortBy = q.SortBy, Descending = q.Descending, Page = q.Page, PageSize = q.PageSize, EmployeeId = employeeId };
    private static LeaveRequestReportRow ToRequestRow(RequestRow x) => new(x.Id, x.EmployeeCode, x.EmployeeName, x.LeaveTypeName, x.StartDate, x.EndDate, x.Quantity, x.Status, x.SubmittedAtUtc, x.DepartmentName, x.WorkLocationName, x.ManagerName, x.LeavePeriodName);
    private static LeaveBalanceReportRow ToBalanceRow(BalanceRow x, IReadOnlyDictionary<Guid, HistoryRow> histories) { histories.TryGetValue(x.EmployeeId, out var h); return new(x.EmployeeId, x.EmployeeCode, x.EmployeeName, h?.Department, h?.WorkLocation, x.LeaveTypeName, x.Mode, x.Granted, x.Reserved, x.Consumed, x.Available, null, null); }
    private bool Tenant(out Guid id) { id = _tenant.TenantId ?? Guid.Empty; return id != Guid.Empty; }
    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime.Date);
    private sealed record RequestRow(Guid Id, Guid EmployeeId, LeaveRequestStatus Status, DateOnly StartDate, DateOnly EndDate, decimal Quantity, DateTime? SubmittedAtUtc, Guid LeaveTypeId, string LeaveTypeName, string EmployeeCode, string FirstName, string? MiddleName, string LastName, Guid? DepartmentId, string? DepartmentName, Guid? WorkLocationId, string? WorkLocationName, string? ManagerName, string LeavePeriodName) { public string EmployeeName => string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x))); }
    private sealed record BalanceRow(Guid EmployeeId, string EmployeeCode, string FirstName, string? MiddleName, string LastName, string LeaveTypeName, Guid LeaveTypeId, EntitlementMode Mode, decimal Granted, decimal Reserved, decimal Consumed, decimal Available, Guid LeavePeriodId) { public string EmployeeName => string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x))); }
    private sealed record HistoryRow(Guid EmployeeId, Guid? DepartmentId, string? Department, Guid? WorkLocationId, string? WorkLocation);
}

file static class LeaveReportQueryExtensions
{
    public static bool IsActiveOnly(this LeaveReportQuery query) => query.Status is null;
}
