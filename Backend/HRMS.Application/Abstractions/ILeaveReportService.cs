using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed class LeaveReportQuery
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public Guid? EmployeeId { get; init; }
    public Guid? LeaveTypeId { get; init; }
    public Guid? LeavePeriodId { get; init; }
    public LeaveRequestStatus? Status { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? WorkLocationId { get; init; }
    public string? OrganizationDimension { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public interface ILeaveReportService
{
    Task<Result<PagedResult<LeaveRequestReportRow>>> GetRequestsAsync(LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<LeaveRequestReportRow>>> GetEmployeeHistoryAsync(Guid employeeId, LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<LeaveBalanceReportRow>>> GetBalancesAsync(LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<LeaveUsageReportRow>>> GetUsageAsync(LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<LeaveAccountingReportRow>>> GetAccountingAsync(LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<PendingApprovalReportRow>>> GetPendingAsync(LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<LeaveOrganizationReportRow>>> GetOrganizationAsync(LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<LeaveCalendarReportRow>>> GetCalendarAsync(LeaveReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<LeaveReportExportDto>> ExportCsvAsync(string report, LeaveReportQuery query, CancellationToken cancellationToken = default);
}

public sealed record LeaveRequestReportRow(Guid RequestId, string EmployeeCode, string EmployeeName, string LeaveType, DateOnly StartDate, DateOnly EndDate, decimal Quantity, LeaveRequestStatus Status, DateTime? SubmittedAtUtc, string? Department, string? WorkLocation, string? Manager, string? LeavePeriod);
public sealed record LeaveBalanceReportRow(Guid EmployeeId, string EmployeeCode, string EmployeeName, string? Department, string? WorkLocation, string LeaveType, EntitlementMode EntitlementMode, decimal? Granted, decimal? Reserved, decimal? Consumed, decimal? Available, decimal? CarryForward, decimal? ExpiringCarryForward);
public sealed record LeaveUsageReportRow(string Group, string? LeaveType, int RequestCount, int EmployeeCount, decimal Quantity);
public sealed record LeaveAccountingReportRow(Guid EmployeeId, string EmployeeCode, string EmployeeName, string LeaveType, string LeavePeriod, DateOnly Date, string EventType, decimal Quantity, LeaveBalanceSourceType SourceType, string? SourceReference, decimal? CalculatedQuantity, decimal? CreditedQuantity, decimal? LapsedQuantity);
public sealed record PendingApprovalReportRow(Guid RequestId, string EmployeeCode, string EmployeeName, string LeaveType, DateOnly StartDate, DateOnly EndDate, decimal Quantity, DateTime? PendingSinceUtc, int DaysPending, string? Manager, string? Department, string? WorkLocation, string AgingBucket);
public sealed record LeaveOrganizationReportRow(string Group, int EmployeeCount, int RequestCount, int PendingCount, decimal ApprovedQuantity);
public sealed record LeaveCalendarReportRow(string Kind, string Name, DateOnly? Date, DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid? CountryId, Guid? WorkLocationId, string? Weekdays, bool IsActive);
public sealed record LeaveReportExportDto(string FileName, string ContentType, byte[] Content, int RowCount);
