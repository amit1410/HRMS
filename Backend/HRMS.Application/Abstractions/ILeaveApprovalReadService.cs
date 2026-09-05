using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveApprovalReadService
{
    Task<Result<PagedResult<LeaveApprovalListItemDto>>> GetInboxAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<LeaveApprovalDetailDto>> GetByIdAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public sealed record LeaveApprovalListItemDto(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    LeaveRequestStatus Status,
    DateTime? SubmittedAtUtc);

public sealed record LeaveApprovalDetailDto(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    LeaveRequestStatus Status,
    DateTime? SubmittedAtUtc,
    Guid LeavePeriodId,
    string LeavePeriodCode,
    string LeavePeriodName,
    Guid LeavePolicyVersionId,
    IReadOnlyList<LeaveRequestDetailDayDto> RequestDays,
    IReadOnlyList<LeaveRequestEventDto> Events);
