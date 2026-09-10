using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IHrLeaveDashboardService
{
    Task<Result<HrLeaveDashboardSummaryDto>> GetSummaryAsync(
        HrLeaveDashboardFilter filter,
        CancellationToken cancellationToken = default);
}

public sealed record HrLeaveDashboardFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? LeaveTypeId = null,
    Guid? DepartmentId = null,
    Guid? WorkLocationId = null);

public sealed record HrLeaveDashboardSummaryDto(
    DateOnly From,
    DateOnly To,
    string? CurrentLeavePeriodName,
    HrLeaveDashboardKpis Kpis,
    IReadOnlyList<HrLeaveDashboardStatusDto> StatusBreakdown,
    IReadOnlyList<HrLeaveDashboardLeaveTypeUsageDto> LeaveTypeUsage,
    IReadOnlyList<HrLeaveDashboardBalanceDto> Balances,
    IReadOnlyList<HrLeaveDashboardApprovalAgingDto> ApprovalAging,
    IReadOnlyList<HrLeaveDashboardPendingDto> PendingApprovals,
    IReadOnlyList<HrLeaveDashboardAbsenceDto> UpcomingAbsences,
    IReadOnlyList<HrLeaveDashboardGroupDto> Departments,
    IReadOnlyList<HrLeaveDashboardGroupDto> WorkLocations,
    IReadOnlyList<HrLeaveDashboardTrendDto> Trend);

public sealed record HrLeaveDashboardKpis(
    int EmployeesOnLeaveToday,
    int ActiveEmployeeCount,
    int UpcomingApprovedRequests,
    int PendingApprovalRequests,
    int ApprovedRequests,
    int RejectedRequests,
    int CancelledOrWithdrawnRequests);

public sealed record HrLeaveDashboardStatusDto(LeaveRequestStatus Status, int RequestCount, decimal Quantity);
public sealed record HrLeaveDashboardLeaveTypeUsageDto(Guid LeaveTypeId, string Code, string Name, int RequestCount, decimal Quantity);
public sealed record HrLeaveDashboardBalanceDto(Guid LeaveTypeId, string Code, string Name, EntitlementMode EntitlementMode, decimal? Granted, decimal? Reserved, decimal? Consumed, decimal? Available);
public sealed record HrLeaveDashboardApprovalAgingDto(string Bucket, int RequestCount);
public sealed record HrLeaveDashboardPendingDto(Guid RequestId, string EmployeeCode, string EmployeeName, string LeaveTypeName, DateOnly StartDate, DateOnly EndDate, decimal Quantity, DateTime? SubmittedAtUtc, int DaysPending, string? ManagerName);
public sealed record HrLeaveDashboardAbsenceDto(Guid RequestId, string EmployeeCode, string EmployeeName, string LeaveTypeName, DateOnly StartDate, DateOnly EndDate, decimal Quantity, string? Department, string? WorkLocation);
public sealed record HrLeaveDashboardGroupDto(string Name, int EmployeeCount, decimal Quantity);
public sealed record HrLeaveDashboardTrendDto(string Period, decimal Quantity);
