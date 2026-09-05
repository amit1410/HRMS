using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveRequestReadService
{
    Task<Result<PagedResult<LeaveRequestListItemDto>>> GetMineAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<LeaveRequestDetailDto>> GetMineByIdAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public sealed record LeaveRequestListItemDto(
    Guid RequestId,
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
    Guid LeavePolicyVersionId);

public sealed record LeaveRequestDetailDto(
    Guid RequestId,
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

public sealed record LeaveRequestDetailDayDto(
    DateOnly Date,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    string? DayClassification,
    string? CalculationReason,
    bool IsEmployeeRequested);

public sealed record LeaveRequestEventDto(
    LeaveRequestEventType EventType,
    DateTime OccurredAtUtc);
