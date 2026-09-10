using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class HrLeaveDashboardService : IHrLeaveDashboardService
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILeavePeriodResolver _periodResolver;
    private readonly TimeProvider _clock;

    public HrLeaveDashboardService(IHrmsDbContext db, ITenantContext tenant, ILeavePeriodResolver periodResolver, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _periodResolver = periodResolver;
        _clock = clock;
    }

    public async Task<Result<HrLeaveDashboardSummaryDto>> GetSummaryAsync(HrLeaveDashboardFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenant.TenantId.HasValue)
            return Result<HrLeaveDashboardSummaryDto>.Unauthorized("A tenant could not be resolved.");

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().DateTime);
        var periodResolution = await _periodResolver.ResolveAsync(_tenant.TenantId.Value, today, cancellationToken);
        var currentPeriod = periodResolution.Status == LeavePeriodResolutionStatus.Resolved ? periodResolution.Period : null;
        var from = filter.From ?? currentPeriod?.StartDate ?? today.AddDays(-30);
        var to = filter.To ?? currentPeriod?.EndDate ?? today.AddDays(30);
        if (to < from)
            return Result<HrLeaveDashboardSummaryDto>.Invalid("to", "The dashboard end date must not be before the start date.");
        if (to.DayNumber - from.DayNumber > 366)
            return Result<HrLeaveDashboardSummaryDto>.Invalid("to", "The dashboard range cannot exceed 366 days.");

        var requests = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == _tenant.TenantId.Value && x.StartDate <= to && x.EndDate >= from &&
                (!filter.LeaveTypeId.HasValue || x.LeaveTypeId == filter.LeaveTypeId.Value))
            .Select(x => new RequestRow(
                x.Id, x.EmployeeId, x.Status, x.StartDate, x.EndDate, x.ChargeableQuantity, x.SubmittedAtUtc,
                x.LeaveTypeId, x.LeaveType!.Code, x.LeaveType.Name, x.Employee!.EmployeeCode ?? string.Empty,
                x.Employee.FirstName, x.Employee.MiddleName, x.Employee.LastName,
                x.EmployeeEmploymentHistory!.DepartmentId, x.EmployeeEmploymentHistory.Department != null ? x.EmployeeEmploymentHistory.Department.Name : x.EmployeeEmploymentHistory.DepartmentName,
                x.EmployeeEmploymentHistory.WorkLocationId, x.EmployeeEmploymentHistory.WorkLocation!.Name,
                x.EmployeeEmploymentHistory.ManagerName))
            .ToListAsync(cancellationToken);

        requests = requests.Where(x => (!filter.DepartmentId.HasValue || x.DepartmentId == filter.DepartmentId) &&
                                       (!filter.WorkLocationId.HasValue || x.WorkLocationId == filter.WorkLocationId)).ToList();
        var approved = requests.Where(x => x.Status == LeaveRequestStatus.Approved).ToList();
        var onLeaveToday = approved.Where(x => x.StartDate <= today && x.EndDate >= today).Select(x => x.EmployeeId).Distinct().Count();
        var activeEmployees = await _db.EmployeeEmploymentHistory.AsNoTracking()
            .Where(x => x.TenantId == _tenant.TenantId.Value && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today) && x.EmploymentStatus == EmployeeStatus.Active)
            .Select(x => x.EmployeeId).Distinct().CountAsync(cancellationToken);
        var upcomingEnd = today.AddDays(30);
        var upcoming = approved.Where(x => x.StartDate >= today && x.StartDate <= upcomingEnd).OrderBy(x => x.StartDate).ThenBy(x => x.EmployeeName).ToList();
        var pending = requests.Where(x => x.Status == LeaveRequestStatus.PendingApproval).OrderBy(x => x.SubmittedAtUtc).ThenBy(x => x.StartDate).ToList();

        var status = requests.GroupBy(x => x.Status).Select(g => new HrLeaveDashboardStatusDto(g.Key, g.Count(), g.Sum(x => x.ChargeableQuantity))).OrderBy(x => x.Status).ToList();
        var usage = approved.GroupBy(x => new { x.LeaveTypeId, x.LeaveTypeCode, x.LeaveTypeName }).Select(g => new HrLeaveDashboardLeaveTypeUsageDto(g.Key.LeaveTypeId, g.Key.LeaveTypeCode, g.Key.LeaveTypeName, g.Count(), g.Sum(x => x.ChargeableQuantity))).OrderByDescending(x => x.Quantity).ToList();
        var departments = approved.GroupBy(x => x.DepartmentName ?? "Unassigned").Select(g => new HrLeaveDashboardGroupDto(g.Key, g.Select(x => x.EmployeeId).Distinct().Count(), g.Sum(x => x.ChargeableQuantity))).OrderByDescending(x => x.Quantity).ToList();
        var locations = approved.GroupBy(x => x.WorkLocationName ?? "Unassigned").Select(g => new HrLeaveDashboardGroupDto(g.Key, g.Select(x => x.EmployeeId).Distinct().Count(), g.Sum(x => x.ChargeableQuantity))).OrderByDescending(x => x.Quantity).ToList();

        var balances = await _db.EmployeeLeaveBalances.AsNoTracking()
            .Where(x => x.TenantId == _tenant.TenantId.Value && (!filter.LeaveTypeId.HasValue || x.LeaveTypeId == filter.LeaveTypeId.Value))
            .GroupBy(x => new { x.LeaveTypeId, Code = x.LeaveType!.Code, Name = x.LeaveType.Name })
            .Select(g => new HrLeaveDashboardBalanceDto(g.Key.LeaveTypeId, g.Key.Code, g.Key.Name, EntitlementMode.Allocated,
                g.Sum(x => x.GrantedQuantity), g.Sum(x => x.ReservedQuantity), g.Sum(x => x.ConsumedQuantity),
                g.Sum(x => x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity)))
            .ToListAsync(cancellationToken);
        var unlimitedIds = await (from rule in _db.LeavePolicyRules.AsNoTracking()
                                  join version in _db.LeavePolicyVersions.AsNoTracking() on rule.LeavePolicyVersionId equals version.Id
                                  join entitlement in _db.LeavePolicyEntitlementRules.AsNoTracking() on rule.Id equals entitlement.LeavePolicyRuleId
                                  where rule.TenantId == _tenant.TenantId.Value && rule.IsActive && version.Status == LeavePolicyVersionStatus.Published &&
                                        version.EffectiveFrom <= today && (version.EffectiveTo == null || version.EffectiveTo >= today) &&
                                        entitlement.EntitlementMode == EntitlementMode.Unlimited && (!filter.LeaveTypeId.HasValue || rule.LeaveTypeId == filter.LeaveTypeId.Value)
                                  select rule.LeaveTypeId).Distinct().ToListAsync(cancellationToken);
        var unlimitedTypes = await _db.LeaveTypes.AsNoTracking().Where(x => x.TenantId == _tenant.TenantId.Value && unlimitedIds.Contains(x.Id)).Select(x => new HrLeaveDashboardBalanceDto(x.Id, x.Code, x.Name, EntitlementMode.Unlimited, null, null, null, null)).ToListAsync(cancellationToken);
        balances.AddRange(unlimitedTypes.Where(x => balances.All(b => b.LeaveTypeId != x.LeaveTypeId)));

        var now = _clock.GetUtcNow().UtcDateTime;
        var aging = new[] { ("0-1 day", 0), ("2-3 days", 0), ("4-7 days", 0), ("8+ days", 0) }.ToDictionary(x => x.Item1, x => x.Item2);
        foreach (var item in pending)
        {
            var days = item.SubmittedAtUtc.HasValue ? Math.Max(0, (now.Date - item.SubmittedAtUtc.Value.Date).Days) : 0;
            aging[days <= 1 ? "0-1 day" : days <= 3 ? "2-3 days" : days <= 7 ? "4-7 days" : "8+ days"]++;
        }
        var trend = approved.GroupBy(x => new { x.StartDate.Year, x.StartDate.Month }).Select(g => new HrLeaveDashboardTrendDto($"{g.Key.Year:D4}-{g.Key.Month:D2}", g.Sum(x => x.ChargeableQuantity))).OrderBy(x => x.Period).ToList();
        var pendingItems = pending.Take(10).Select(x => new HrLeaveDashboardPendingDto(x.Id, x.EmployeeCode, x.EmployeeName, x.LeaveTypeName, x.StartDate, x.EndDate, x.ChargeableQuantity, x.SubmittedAtUtc, x.SubmittedAtUtc.HasValue ? Math.Max(0, (now.Date - x.SubmittedAtUtc.Value.Date).Days) : 0, x.ManagerName)).ToList();
        var absenceItems = upcoming.Take(20).Select(x => new HrLeaveDashboardAbsenceDto(x.Id, x.EmployeeCode, x.EmployeeName, x.LeaveTypeName, x.StartDate, x.EndDate, x.ChargeableQuantity, x.DepartmentName, x.WorkLocationName)).ToList();
        return Result<HrLeaveDashboardSummaryDto>.Success(new(from, to, currentPeriod?.Name,
            new(onLeaveToday, activeEmployees, upcoming.Count, pending.Count, approved.Count, requests.Count(x => x.Status == LeaveRequestStatus.Rejected), requests.Count(x => x.Status is LeaveRequestStatus.Cancelled or LeaveRequestStatus.Withdrawn)),
            status, usage, balances.OrderBy(x => x.Code).ToList(), aging.Select(x => new HrLeaveDashboardApprovalAgingDto(x.Key, x.Value)).ToList(), pendingItems, absenceItems, departments, locations, trend));
    }

    private sealed record RequestRow(Guid Id, Guid EmployeeId, LeaveRequestStatus Status, DateOnly StartDate, DateOnly EndDate, decimal ChargeableQuantity, DateTime? SubmittedAtUtc, Guid LeaveTypeId, string LeaveTypeCode, string LeaveTypeName, string EmployeeCode, string FirstName, string? MiddleName, string LastName, Guid? DepartmentId, string? DepartmentName, Guid? WorkLocationId, string? WorkLocationName, string? ManagerName)
    {
        public string EmployeeName => string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }
}
